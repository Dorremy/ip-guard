using System.Diagnostics;
using System.Runtime.InteropServices;

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

public static class ProgramCloser
{
    private const uint WmClose = 0x0010;

    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        "IpGuard", "explorer", "dwm", "winlogon", "csrss", "svchost", "services", "lsass", "smss", "wininit",
        "System", "Idle", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "SearchHost", "StartMenuExperienceHost",
        "ShellExperienceHost", "RuntimeBroker", "LockApp", "TextInputHost"
    };

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

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

    public static IReadOnlyList<RunningProgram> RunningWithWindows()
    {
        var result = new Dictionary<string, RunningProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle) || IsProtected(process.ProcessName))
                {
                    continue;
                }

                result.TryAdd(process.ProcessName, new RunningProgram(process.ProcessName, process.MainWindowTitle));
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.Values.OrderBy(program => program.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static CloseReport Close(IEnumerable<string> names)
    {
        var asked = new List<string>();
        var notRunning = new List<string>();
        var noWindow = new List<string>();
        var skipped = new List<string>();

        foreach (var name in names.Select(Normalize).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsProtected(name))
            {
                skipped.Add(name);
                continue;
            }

            var processes = Process.GetProcessesByName(name);
            if (processes.Length == 0)
            {
                notRunning.Add(name);
                continue;
            }

            var posted = 0;
            foreach (var process in processes)
            {
                posted += AskToClose(process.Id);
                process.Dispose();
            }

            (posted > 0 ? asked : noWindow).Add(name);
        }

        return new CloseReport(asked, notRunning, noWindow, skipped);
    }

    public static int AskToClose(int processId)
    {
        var posted = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == (uint)processId && IsWindowVisible(window) && PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero))
            {
                posted++;
            }

            return true;
        }, IntPtr.Zero);
        return posted;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
