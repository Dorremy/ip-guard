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
    private static readonly Uri ProxyProbe = new("https://api.2ip.io/");

    private static readonly (string Url, Func<string, IpInfo?> Parse)[] DefaultSources =
    {
        ("https://api.2ip.io/", ParseTwoIp),
        ("https://ipwho.is/", ParseIpWho)
    };

    private readonly HttpClient http;
    private readonly (string Url, Func<string, IpInfo?> Parse)[] sources;

    public IpLookup() : this(DefaultSources, CreateClient(true))
    {
    }

    public IpLookup((string Url, Func<string, IpInfo?> Parse)[] sources) : this(sources, CreateClient(true))
    {
    }

    private IpLookup((string Url, Func<string, IpInfo?> Parse)[] sources, HttpClient http)
    {
        this.sources = sources;
        this.http = http;
    }

    public static IpLookup Direct() => new(DefaultSources, CreateClient(false));

    public static bool SystemProxyActive()
    {
        var proxy = HttpClient.DefaultProxy.GetProxy(ProxyProbe);
        return proxy is not null && proxy != ProxyProbe;
    }

    public async Task<IpInfo?> GetAsync()
    {
        foreach (var (url, parse) in sources)
        {
            try
            {
                var body = await http.GetStringAsync(url);
                var info = parse(body);
                if (info is not null)
                {
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

    public static string? ParseIpOnly(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ip = Text(doc.RootElement, "ip");
        return IPAddress.TryParse(ip, out _) ? ip : null;
    }

    internal static HttpClient CreateClient(bool useProxy, bool ipv6Only = false)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = useProxy,
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
