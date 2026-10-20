namespace IpGuard;

public enum Status
{
    Unavailable,
    Home,
    NoReference,
    Unchanged,
    Changed,
    VpnOff,
    Ready,
    Leak
}

public sealed record IpInfo(string Ip, string CountryCode, string Country, string Provider);

public sealed record Verdict(Status Status, string Title, string Message, bool Notify);

public sealed record HistoryEntry(DateTime Time, string Ip, string CountryCode, string Country, string Provider, Status Status, string? Note = null);

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
    public bool SoundEnabled { get; set; } = true;
    public bool CheckDirectRoute { get; set; } = true;
    public bool CheckIpv6 { get; set; } = true;
    public bool AlertWindow { get; set; } = true;
    public int OutageSeconds { get; set; } = 30;
    public int SoundVolume { get; set; } = 100;
    public Dictionary<string, int> Sounds { get; set; } = new();

    public void CopyFrom(Settings other)
    {
        HomeCountryCode = other.HomeCountryCode;
        IntervalSeconds = other.IntervalSeconds;
        SoundEnabled = other.SoundEnabled;
        CheckDirectRoute = other.CheckDirectRoute;
        CheckIpv6 = other.CheckIpv6;
        AlertWindow = other.AlertWindow;
        OutageSeconds = other.OutageSeconds;
        SoundVolume = other.SoundVolume;
        Sounds = new Dictionary<string, int>(other.Sounds);
    }

    public int SoundVariant(string key) =>
        Sounds.TryGetValue(key, out var variant) && variant >= 1 && variant <= IpGuard.Sounds.Variants ? variant : 1;
}

public static class Labels
{
    public static string For(Status status) => status switch
    {
        Status.Unavailable => "Нет связи",
        Status.Home => "Без VPN",
        Status.NoReference => "Эталон не задан",
        Status.Unchanged => "IP совпадает",
        Status.Changed => "IP изменился",
        Status.VpnOff => "VPN отключился",
        Status.Ready => "Эталон сохранён",
        Status.Leak => "Утечка мимо VPN",
        _ => status.ToString()
    };
}
