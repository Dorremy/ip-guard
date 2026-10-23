using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpGuard;

public enum WatchMode
{
    Own,
    Client,
    Idle
}

public enum StatusFile
{
    Missing,
    Unreadable,
    Read
}

public sealed record WatchModel(
    WatchMode Mode,
    bool Windows,
    bool Interactive,
    StatusFile File,
    StatusReport? Report,
    bool Starting,
    DateTime NowUtc,
    IReadOnlyList<HistoryEntry> History,
    IReadOnlyList<string> Journal,
    string? Prompt = null,
    string? Notice = null)
{
    private static readonly JsonSerializerOptions StateOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public TimeSpan Age => Report is null ? TimeSpan.Zero : StatusCommand.AgeOf(Report, NowUtc);

    public bool Stale => Report is not null && Age >= StatusCommand.FreshFor;

    public bool Healthy => File == StatusFile.Read && Report is { Ok: true } && !Stale && !Starting;

    public static WatchModel Load(
        DataPaths paths,
        DateTime nowUtc,
        WatchMode mode,
        bool windows,
        bool interactive,
        DateTime? startedUtc,
        int historyCount,
        int journalCount)
    {
        var file = StatusFile.Missing;
        StatusReport? report = null;
        if (System.IO.File.Exists(paths.Status))
        {
            file = StatusCommand.TryRead(paths.Status, out var read) ? StatusFile.Read : StatusFile.Unreadable;
            report = file == StatusFile.Read ? read : null;
        }

        var starting = mode == WatchMode.Own
            && startedUtc is { } started
            && (file == StatusFile.Missing || report is not null && report.UpdatedUtc < started);
        return new WatchModel(
            mode,
            windows,
            interactive,
            file,
            report,
            starting,
            nowUtc,
            ReadHistory(paths.State, historyCount),
            new Journal(paths.Log).Tail(journalCount));
    }

    private static IReadOnlyList<HistoryEntry> ReadHistory(string path, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<HistoryEntry>();
        }

        try
        {
            if (!System.IO.File.Exists(path))
            {
                return Array.Empty<HistoryEntry>();
            }

            var state = JsonSerializer.Deserialize<MonitorState>(System.IO.File.ReadAllText(path), StateOptions);
            return state?.History is { } history ? history.AsEnumerable().Reverse().Take(count).ToList() : Array.Empty<HistoryEntry>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<HistoryEntry>();
        }
    }
}
