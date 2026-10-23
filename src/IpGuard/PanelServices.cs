namespace IpGuard;

public sealed class PanelServices
{
    public Func<Settings> GetSettings { get; init; } = () => new Settings();

    public Func<bool> GetAutostart { get; init; } = () => false;

    public Action<Settings, bool> ApplySettings { get; init; } = (_, _) => { };

    public Func<MonitorState> GetState { get; init; } = () => new MonitorState();

    public Action ReferencesChanged { get; init; } = () => { };

    public Func<IpInfo?> GetCurrentIp { get; init; } = () => null;

    public Func<string> GetHome { get; init; } = () => "RU";

    public Func<IReadOnlyList<RunningProgram>> GetPrograms { get; init; } = () => Array.Empty<RunningProgram>();

    public Action<string, int, int> PlaySound { get; init; } = (_, _, _) => { };

    public Func<IReadOnlyList<HistoryEntry>> GetHistory { get; init; } = () => Array.Empty<HistoryEntry>();

    public Action ClearHistory { get; init; } = () => { };

    public Func<string?> PickSavePath { get; init; } = () => null;

    public Func<int> HistoryStamp { get; init; } = () => 0;

    public Func<IReadOnlyList<string>> ReadJournal { get; init; } = () => Array.Empty<string>();

    public Func<long> JournalStamp { get; init; } = () => 0;

    public Action OpenJournalFile { get; init; } = () => { };

    public Action<string> CopyText { get; init; } = ClipboardText.TrySet;

    public Action TestAlert { get; init; } = () => { };

    public Func<string> Version { get; init; } = () => "";

    public Func<IReadOnlyList<string>> GetRoutes { get; init; } = () => Array.Empty<string>();

    public Func<DateTime?> GetQuietUntil { get; init; } = () => null;

    public Action<int> SetQuiet { get; init; } = _ => { };

    public Func<string?> GetUpdateTag { get; init; } = () => null;

    public Action OpenUpdate { get; init; } = () => { };

    public Action CheckUpdates { get; init; } = () => { };

    public Action Restart { get; init; } = () => { };

    public Action Exit { get; init; } = () => { };

    public Action ExportBackup { get; init; } = () => { };

    public Action ImportBackup { get; init; } = () => { };
}

public static class ClipboardText
{
    public static void TrySet(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }
}
