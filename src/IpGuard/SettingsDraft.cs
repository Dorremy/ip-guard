namespace IpGuard;

public enum ProgramAdd
{
    Added,
    Empty,
    Protected,
    Duplicate
}

public sealed class SettingsDraft
{
    public static readonly (int Seconds, string Title)[] OutageChoices =
    {
        (0, "Сразу"), (15, "Через 15 с"), (30, "Через 30 с"), (60, "Через минуту")
    };

    public SettingsDraft(Settings current, bool autostart)
    {
        Working = SettingsRules.Clone(current);
        Autostart = autostart;
    }

    public Settings Working { get; }

    public bool Autostart { get; set; }

    public IReadOnlyList<string> Programs => Working.ProtectPrograms;

    public ProgramAdd AddProgram(string? name)
    {
        var clean = ProgramCloser.Normalize(name);
        if (clean.Length == 0)
        {
            return ProgramAdd.Empty;
        }

        if (ProgramCloser.IsProtected(clean))
        {
            return ProgramAdd.Protected;
        }

        if (Working.ProtectPrograms.Any(item => string.Equals(item, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return ProgramAdd.Duplicate;
        }

        Working.ProtectPrograms.Add(clean);
        return ProgramAdd.Added;
    }

    public bool RemoveProgram(int index)
    {
        if (index < 0 || index >= Working.ProtectPrograms.Count)
        {
            return false;
        }

        Working.ProtectPrograms.RemoveAt(index);
        return true;
    }

    public Settings Result()
    {
        Working.IntervalSeconds = SettingsRules.ClampInterval(Working.IntervalSeconds);
        Working.StatusPort = SettingsRules.ClampPort(Working.StatusPort);
        Working.ProtectOutageSeconds = Math.Clamp(Working.ProtectOutageSeconds, 5, 3600);
        Working.ProtectGraceSeconds = Math.Clamp(Working.ProtectGraceSeconds, 0, 120);
        Working.SoundVolume = Math.Clamp(Working.SoundVolume, 0, 100);
        if (OutageChoices.All(choice => choice.Seconds != Working.OutageSeconds))
        {
            Working.OutageSeconds = 30;
        }

        return Working;
    }
}
