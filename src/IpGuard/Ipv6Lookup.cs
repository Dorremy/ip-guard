using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace IpGuard;

public sealed class Ipv6Lookup : IIpLookup
{
    private readonly HttpClient v6 = IpLookup.CreateClient(null, true);
    private readonly string addressUrl;
    private readonly Func<string, string> geoUrl;
    private IpInfo? cached;

    public Ipv6Lookup(string addressUrl = "https://api6.ipify.org/?format=json", Func<string, string>? geoUrl = null)
    {
        this.addressUrl = addressUrl;
        this.geoUrl = geoUrl ?? (ip => $"https://ipwho.is/{ip}");
    }

    public async Task<IpInfo?> GetAsync()
    {
        string? ip;
        try
        {
            ip = IpLookup.ParseIpOnly(await v6.GetStringAsync(addressUrl));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }

        if (ip is null || !IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        if (cached?.Ip == ip)
        {
            return cached;
        }

        try
        {
            using var geo = IpLookup.CreateClient(SystemProxy.Current());
            cached = IpLookup.ParseIpWho(await geo.GetStringAsync(geoUrl(ip)));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            cached = null;
        }

        return cached ?? new IpInfo(ip, "", "", "");
    }
}
