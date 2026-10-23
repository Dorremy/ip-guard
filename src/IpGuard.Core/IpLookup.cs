using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace IpGuard;

public interface IIpLookup
{
    Task<IpInfo?> GetAsync();
}

public sealed class IpLookup : IIpLookup
{
    private static readonly TimeSpan LimitPause = TimeSpan.FromMinutes(30);

    private static readonly (string Url, Func<string, IpInfo?> Parse)[] DefaultSources =
    {
        ("https://api.2ip.io/", ParseTwoIp),
        ("https://ipwho.is/", ParseIpWho),
        ("https://ipinfo.io/json", ParseIpInfo),
        ("https://ifconfig.co/json", ParseIfconfig)
    };

    private readonly Func<IWebProxy?> proxy;
    private readonly (string Url, Func<string, IpInfo?> Parse)[] sources;
    private readonly DateTime[] pausedUntil;
    private readonly Func<DateTime> now;
    private int next;

    public IpLookup() : this(DefaultSources, SystemProxy.Current)
    {
    }

    public IpLookup((string Url, Func<string, IpInfo?> Parse)[] sources) : this(sources, SystemProxy.Current)
    {
    }

    public IpLookup((string Url, Func<string, IpInfo?> Parse)[] sources, Func<IWebProxy?> proxy, Func<DateTime>? now = null)
    {
        this.sources = sources;
        this.proxy = proxy;
        this.now = now ?? (() => DateTime.UtcNow);
        pausedUntil = new DateTime[sources.Length];
    }

    public IReadOnlyList<string> PausedSources =>
        sources.Where((_, i) => pausedUntil[i] > now()).Select(source => new Uri(source.Url).Host).ToList();

    public static IpLookup Direct() => new(DefaultSources, () => null);

    public static bool SystemProxyActive() => SystemProxy.Current() is not null;

    public async Task<IpInfo?> GetAsync()
    {
        using var http = CreateClient(proxy());
        var order = Enumerable.Range(0, sources.Length).Select(k => (next + k) % sources.Length).ToList();
        var available = order.Where(i => pausedUntil[i] <= now()).ToList();
        if (available.Count == 0)
        {
            available = order.OrderBy(i => pausedUntil[i]).Take(1).ToList();
        }

        foreach (var i in available)
        {
            var (url, parse) = sources[i];
            try
            {
                using var response = await http.GetAsync(url);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    pausedUntil[i] = now() + LimitPause;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var info = parse(await response.Content.ReadAsStringAsync());
                if (info is not null)
                {
                    pausedUntil[i] = default;
                    next = (i + 1) % sources.Length;
                    return info;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
            }
        }

        return null;
    }

    public static IpInfo? ParseTwoIp(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var provider = root.TryGetProperty("asn", out var asn) && asn.ValueKind == JsonValueKind.Object
            ? Text(asn, "name")
            : "";
        return Build(Text(root, "ip"), Text(root, "code"), Text(root, "country"), provider);
    }

    public static IpInfo? ParseIpWho(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        var provider = root.TryGetProperty("connection", out var connection) && connection.ValueKind == JsonValueKind.Object
            ? FirstNonEmpty(Text(connection, "isp"), Text(connection, "org"))
            : "";
        return Build(Text(root, "ip"), Text(root, "country_code"), Text(root, "country"), provider);
    }

    public static IpInfo? ParseIpInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var code = Text(root, "country");
        var org = Text(root, "org");
        var provider = org.StartsWith("AS", StringComparison.Ordinal) && org.Contains(' ') ? org[(org.IndexOf(' ') + 1)..] : org;
        return Build(Text(root, "ip"), code, code, provider);
    }

    public static IpInfo? ParseIfconfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return Build(Text(root, "ip"), Text(root, "country_iso"), Text(root, "country"), Text(root, "asn_org"));
    }

    public static string? ParseIpOnly(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ip = Text(doc.RootElement, "ip");
        return IPAddress.TryParse(ip, out _) ? ip : null;
    }

    internal static HttpClient CreateClient(IWebProxy? proxy, bool ipv6Only = false)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = proxy is not null,
            Proxy = proxy,
            PooledConnectionLifetime = TimeSpan.Zero
        };
        if (ipv6Only)
        {
            handler.ConnectCallback = ConnectIpv6Async;
        }

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(ipv6Only ? 5 : 8) };
        client.DefaultRequestHeaders.ConnectionClose = true;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IpGuard/0.2");
        return client;
    }

    private static async ValueTask<Stream> ConnectIpv6Async(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetworkV6, token);
        if (addresses.Length == 0)
        {
            throw new HttpRequestException("No IPv6 address");
        }

        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static IpInfo? Build(string ip, string code, string country, string provider)
    {
        if (!IPAddress.TryParse(ip, out _) || code.Length != 2)
        {
            return null;
        }

        return new IpInfo(ip, code.ToUpperInvariant(), country, provider);
    }

    private static string FirstNonEmpty(string a, string b) => a.Length > 0 ? a : b;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
