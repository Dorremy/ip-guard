namespace IpGuard;

public enum Status
{
    Unavailable,
    Home,
    Ready,
    Unchanged,
    Changed,
    VpnOff
}

public sealed record IpInfo(string Ip, string CountryCode, string Country, string Provider);

public sealed record Verdict(Status Status, string Title, string Message, bool Notify);

public sealed record HistoryEntry(DateTime Time, string Ip, string CountryCode, string Country, string Provider, Status Status);

public sealed class MonitorState
{
    public Dictionary<string, string> Trusted { get; set; } = new();
    public string? LastIp { get; set; }
    public string? LastCountryCode { get; set; }
    public List<HistoryEntry> History { get; set; } = new();
}

public sealed class Settings
{
    public string HomeCountryCode { get; set; } = "RU";
    public int IntervalSeconds { get; set; } = 15;
}

public static class Labels
{
    public static string For(Status status) => status switch
    {
        Status.Unavailable => "Нет связи",
        Status.Home => "Без VPN",
        Status.Ready => "Новый эталон",
        Status.Unchanged => "IP совпадает",
        Status.Changed => "IP изменился",
        Status.VpnOff => "VPN отключился",
        _ => status.ToString()
    };
}
