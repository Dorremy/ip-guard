using System.Text;

namespace IpGuard;

public sealed class Journal
{
    private readonly object sync = new();
    private readonly long limitBytes;

    public Journal(string path, long limitBytes = 1024 * 1024)
    {
        Path = path;
        this.limitBytes = limitBytes;
    }

    public string Path { get; }

    public string BackupPath => System.IO.Path.ChangeExtension(Path, ".1.txt");

    public IReadOnlyList<string> Tail(int count)
    {
        try
        {
            if (!File.Exists(Path))
            {
                return Array.Empty<string>();
            }

            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var queue = new Queue<string>();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                queue.Enqueue(line);
                if (queue.Count > count)
                {
                    queue.Dequeue();
                }
            }

            return queue.ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public long Stamp()
    {
        try
        {
            var info = new FileInfo(Path);
            return info.Exists ? info.Length * 31 + info.LastWriteTimeUtc.Ticks : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message.Replace("\n", " / ")}{Environment.NewLine}";
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length + line.Length > limitBytes)
                {
                    File.Move(Path, BackupPath, true);
                }

                File.AppendAllText(Path, line, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
