namespace IpGuard;

public enum ProtectionReason
{
    Alarm,
    Outage
}

public sealed class ProtectionTrigger
{
    private bool fired;

    public ProtectionReason? Evaluate(Settings settings, Status status, TimeSpan? outage)
    {
        if (status is not (Status.Changed or Status.Leak or Status.Unavailable))
        {
            fired = false;
            return null;
        }

        if (!settings.ProtectEnabled || settings.ProtectPrograms.Count == 0 || fired)
        {
            return null;
        }

        ProtectionReason? reason = null;
        if (status is Status.Changed or Status.Leak)
        {
            if (settings.ProtectOnAlarm)
            {
                reason = ProtectionReason.Alarm;
            }
        }
        else if (settings.ProtectOnOutage && outage is { } lasted && lasted >= TimeSpan.FromSeconds(Math.Max(0, settings.ProtectOutageSeconds)))
        {
            reason = ProtectionReason.Outage;
        }

        if (reason is not null)
        {
            fired = true;
        }

        return reason;
    }

    public void Reset() => fired = false;
}

public sealed record RunningProgram(string Name, string Title);

public sealed record CloseReport(IReadOnlyList<string> Asked, IReadOnlyList<string> NotRunning, IReadOnlyList<string> NoWindow, IReadOnlyList<string> Skipped)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Asked.Count > 0)
            {
                parts.Add($"попросил закрыть: {string.Join(", ", Asked)}");
            }

            if (NoWindow.Count > 0)
            {
                parts.Add($"нет окна: {string.Join(", ", NoWindow)}");
            }

            if (NotRunning.Count > 0)
            {
                parts.Add($"не запущено: {string.Join(", ", NotRunning)}");
            }

            if (Skipped.Count > 0)
            {
                parts.Add($"пропущено (системные): {string.Join(", ", Skipped)}");
            }

            return parts.Count == 0 ? "список программ пуст" : string.Join("; ", parts);
        }
    }
}

public static class ProgramNames
{
    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        "IpGuard", "explorer", "dwm", "winlogon", "csrss", "svchost", "services", "lsass", "smss", "wininit",
        "System", "Idle", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "SearchHost", "StartMenuExperienceHost",
        "ShellExperienceHost", "RuntimeBroker", "LockApp", "TextInputHost"
    };

    public static string Normalize(string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^4];
        }

        return clean.Trim();
    }

    public static bool IsProtected(string name) => Never.Contains(Normalize(name));
}
