namespace IpGuard;

public static class LeakDetector
{
    public static string? Find(IpInfo primary, IpInfo? direct, IpInfo? ipv6, string home)
    {
        var problems = new List<string>();

        if (direct is not null && direct.Ip != primary.Ip)
        {
            var how = direct.CountryCode == home ? "напрямую, мимо VPN" : "с другого адреса";
            problems.Add($"Программы без прокси выходят {how}: {direct.Country} ({direct.CountryCode}), {direct.Ip}. Браузер: {primary.Ip}");
        }

        if (ipv6 is not null && ipv6.CountryCode.Length == 2 && ipv6.CountryCode != primary.CountryCode)
        {
            problems.Add($"IPv6 идёт мимо VPN: {ipv6.Country} ({ipv6.CountryCode}), {ipv6.Ip}");
        }

        return problems.Count == 0 ? null : string.Join("\n", problems);
    }
}
