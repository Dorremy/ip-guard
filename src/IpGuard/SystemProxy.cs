using System.Net;
using Microsoft.Win32;

namespace IpGuard;

public static class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public static IWebProxy? Current()
    {
        if (HasEnvironmentProxy())
        {
            return HttpClient.DefaultProxy;
        }

        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var enabled = key?.GetValue("ProxyEnable") is int value ? value : 0;
        var address = Parse(enabled, key?.GetValue("ProxyServer") as string);
        return address is null ? null : new WebProxy(address) { BypassProxyOnLocal = true };
    }

    public static Uri? Parse(int enabled, string? server)
    {
        if (enabled == 0 || string.IsNullOrWhiteSpace(server))
        {
            return null;
        }

        server = server.Trim();
        if (!server.Contains('='))
        {
            return ToUri("http", server);
        }

        var entries = server
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].ToLowerInvariant(), parts => parts[1]);

        if (entries.TryGetValue("https", out var https))
        {
            return ToUri("http", https);
        }

        if (entries.TryGetValue("http", out var http))
        {
            return ToUri("http", http);
        }

        return entries.TryGetValue("socks", out var socks) ? ToUri("socks4", socks) : null;
    }

    private static Uri? ToUri(string scheme, string hostPort)
    {
        var value = hostPort.Contains("://") ? hostPort : $"{scheme}://{hostPort}";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool HasEnvironmentProxy() =>
        new[] { "HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy" }
            .Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
}
