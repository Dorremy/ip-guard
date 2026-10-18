using System.Net;
using System.Text.Json;

namespace IpGuard;

public interface IIpLookup
{
    Task<IpInfo?> GetAsync();
}

public sealed class IpLookup : IIpLookup
{
    private static readonly (string Url, Func<string, IpInfo?> Parse)[] DefaultSources =
    {
        ("https://api.2ip.io/", ParseTwoIp),
        ("https://ipwho.is/", ParseIpWho)
    };

    private readonly HttpClient http = CreateClient();
    private readonly (string Url, Func<string, IpInfo?> Parse)[] sources;

    public IpLookup() : this(DefaultSources)
    {
    }

    public IpLookup((string Url, Func<string, IpInfo?> Parse)[] sources)
    {
        this.sources = sources;
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

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.Zero };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.ConnectionClose = true;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IpGuard/0.1");
        return client;
    }
}
