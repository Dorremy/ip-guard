using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpGuard;

public enum LoadSource
{
    Main,
    Fresh,
    PreviousVersion,
    Snapshot,
    Default
}

public sealed class JsonStore<T> where T : new()
{
    public const int DefaultKeep = 12;

    public static readonly TimeSpan DefaultSnapshotInterval = TimeSpan.FromHours(2);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object sync = new();
    private readonly string path;
    private readonly string? snapshotsDir;
    private readonly Func<DateTime> now;
    private readonly int keep;
    private readonly TimeSpan interval;

    public JsonStore(string path, string? snapshotsDir = null, Func<DateTime>? now = null, int keep = DefaultKeep, TimeSpan? interval = null)
    {
        this.path = path;
        this.snapshotsDir = snapshotsDir;
        this.now = now ?? (() => DateTime.Now);
        this.keep = Math.Max(1, keep);
        this.interval = interval ?? DefaultSnapshotInterval;
    }

    public LoadSource LastSource { get; private set; } = LoadSource.Fresh;

    public string PreviousPath => path + ".bak";

    public T Load()
    {
        lock (sync)
        {
            var exists = File.Exists(path);
            if (exists && TryRead(path, out var current))
            {
                LastSource = LoadSource.Main;
                return current!;
            }

            if (exists)
            {
                QuarantineBrokenFile();
            }

            if (TryRead(PreviousPath, out var previous))
            {
                Restore(PreviousPath);
                LastSource = LoadSource.PreviousVersion;
                return previous!;
            }

            foreach (var snapshot in Snapshots())
            {
                if (TryRead(snapshot, out var restored))
                {
                    Restore(snapshot);
                    LastSource = LoadSource.Snapshot;
                    return restored!;
                }
            }

            LastSource = exists ? LoadSource.Default : LoadSource.Fresh;
            return new T();
        }
    }

    public void Save(T value)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            KeepPreviousVersion();

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, true);

            TakeSnapshotIfDue();
        }
    }

    public IReadOnlyList<string> Snapshots()
    {
        if (snapshotsDir is null || !Directory.Exists(snapshotsDir))
        {
            return Array.Empty<string>();
        }

        var prefix = Path.GetFileNameWithoutExtension(path) + ".";
        return Directory.GetFiles(snapshotsDir, prefix + "*.json")
            .Where(file => IsSnapshotName(Path.GetFileName(file), prefix))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsSnapshotName(string name, string prefix)
    {
        var middle = name[prefix.Length..^".json".Length];
        return middle.Length == 15 && middle[8] == '-' && middle.Where((c, i) => i != 8).All(char.IsAsciiDigit);
    }

    private bool TryRead(string file, out T? value)
    {
        value = default;
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }

            value = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options);
            return value is not null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void KeepPreviousVersion()
    {
        try
        {
            if (File.Exists(path) && TryRead(path, out _))
            {
                File.Copy(path, PreviousPath, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TakeSnapshotIfDue()
    {
        if (snapshotsDir is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(snapshotsDir);
            var moment = now();
            var latest = Snapshots().FirstOrDefault();
            if (latest is not null)
            {
                var age = moment - SnapshotTime(latest);
                if (age < interval && age > -interval)
                {
                    return;
                }
            }

            var name = $"{Path.GetFileNameWithoutExtension(path)}.{moment:yyyyMMdd-HHmmss}.json";
            File.Copy(path, Path.Combine(snapshotsDir, name), true);
            foreach (var old in Snapshots().Skip(keep))
            {
                File.Delete(old);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static DateTime SnapshotTime(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var stamp = name[(name.LastIndexOf('.') + 1)..];
        return DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var time)
            ? time
            : File.GetLastWriteTime(file);
    }

    private void QuarantineBrokenFile()
    {
        try
        {
            File.Copy(path, $"{path}.{now():yyyyMMdd-HHmmss}.bad", true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Restore(string source)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(source, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
