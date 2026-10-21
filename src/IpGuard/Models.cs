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

public sealed record Reference(string Country, string Ip, string Label = "");

public sealed class MonitorState
{
    public Dictionary<string, string> Trusted { get; set; } = new();
    public List<Reference> References { get; set; } = new();
    public string? LastIp { get; set; }
    public string? LastCountryCode { get; set; }
    public List<HistoryEntry> History { get; set; } = new();

    public void MigrateLegacy()
    {
        foreach (var (country, ip) in Trusted.ToList())
        {
            AddReference(new Reference(country, ip));
        }

        Trusted.Clear();
    }

    public IReadOnlyList<Reference> ReferencesFor(string country)
    {
        MigrateLegacy();
        return References.Where(reference => reference.Country == country).ToList();
    }

    public bool AddReference(Reference reference)
    {
        if (References.Any(item => item.Country == reference.Country && item.Ip == reference.Ip))
        {
            return false;
        }

        References.Add(reference);
        return true;
    }

    public bool RemoveReference(string country, string ip) =>
        References.RemoveAll(item => item.Country == country && item.Ip == ip) > 0;

    public bool RenameReference(string country, string ip, string label)
    {
        var index = References.FindIndex(item => item.Country == country && item.Ip == ip);
        if (index < 0)
        {
            return false;
        }

        References[index] = References[index] with { Label = label.Trim() };
        return true;
    }
}

public sealed class Settings
{
    public string HomeCountryCode { get; set; } = "RU";
    public int IntervalSeconds { get; set; } = 15;
    public bool SoundEnabled { get; set; } = true;
    public bool CheckDirectRoute { get; set; } = true;
    public bool CheckIpv6 { get; set; } = true;
    public bool AlertWindow { get; set; } = true;
    public bool SimpleMenu { get; set; } = false;
    public int OutageSeconds { get; set; } = 30;
    public int SoundVolume { get; set; } = 100;
    public bool StatusServerEnabled { get; set; } = false;
    public int StatusPort { get; set; } = 47831;
    public bool UpdateCheckEnabled { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string NotifiedUpdate { get; set; } = "";
    public bool ProtectEnabled { get; set; } = false;
    public bool ProtectOnAlarm { get; set; } = true;
    public bool ProtectOnOutage { get; set; } = false;
    public int ProtectOutageSeconds { get; set; } = 30;
    public int ProtectGraceSeconds { get; set; } = 10;
    public List<string> ProtectPrograms { get; set; } = new();
    public Dictionary<string, int> Sounds { get; set; } = new();

    public void CopyFrom(Settings other)
    {
        HomeCountryCode = other.HomeCountryCode;
        IntervalSeconds = other.IntervalSeconds;
        SoundEnabled = other.SoundEnabled;
        CheckDirectRoute = other.CheckDirectRoute;
        CheckIpv6 = other.CheckIpv6;
        AlertWindow = other.AlertWindow;
        SimpleMenu = other.SimpleMenu;
        OutageSeconds = other.OutageSeconds;
        SoundVolume = other.SoundVolume;
        StatusServerEnabled = other.StatusServerEnabled;
        StatusPort = other.StatusPort;
        UpdateCheckEnabled = other.UpdateCheckEnabled;
        LastUpdateCheckUtc = other.LastUpdateCheckUtc;
        NotifiedUpdate = other.NotifiedUpdate;
        ProtectEnabled = other.ProtectEnabled;
        ProtectOnAlarm = other.ProtectOnAlarm;
        ProtectOnOutage = other.ProtectOnOutage;
        ProtectOutageSeconds = other.ProtectOutageSeconds;
        ProtectGraceSeconds = other.ProtectGraceSeconds;
        ProtectPrograms = new List<string>(other.ProtectPrograms);
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
