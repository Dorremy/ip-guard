namespace IpGuard;

public sealed class FileStamp
{
    private readonly string path;
    private (long Ticks, long Length)? mark;

    public FileStamp(string path)
    {
        this.path = path;
    }

    public void Mark() => mark = Read();

    public bool Changed() => Read() is { } current && current != mark;

    private (long Ticks, long Length)? Read()
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public static class StateSync
{
    public static void Replace(MonitorState target, MonitorState source)
    {
        target.Trusted = new Dictionary<string, string>(source.Trusted);
        target.References = new List<Reference>(source.References);
        target.LastIp = source.LastIp;
        target.LastCountryCode = source.LastCountryCode;
        target.History = new List<HistoryEntry>(source.History);
        target.MigrateLegacy();
    }

    public static bool MergeReferences(MonitorState target, MonitorState source)
    {
        source.MigrateLegacy();
        var added = false;
        foreach (var reference in source.References)
        {
            added |= target.AddReference(reference);
        }

        return added;
    }
}
