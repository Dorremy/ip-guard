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
