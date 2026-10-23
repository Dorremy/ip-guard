using System.Security.Cryptography;
using System.Text;

namespace IpGuard;

public sealed class InstanceLock : IDisposable
{
    private const string LocalPrefix = "Local\\";
    private const string GlobalPrefix = "Global\\";

    private readonly Mutex mutex;

    private InstanceLock(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static InstanceLock? TryAcquire(string name)
    {
        var mutex = new Mutex(true, Scoped(name), out var created);
        if (created)
        {
            return new InstanceLock(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static string Scoped(string name, bool windows, string user)
    {
        if (windows || !name.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            return name;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(user));
        return $"{GlobalPrefix}{name[LocalPrefix.Length..]}.u{Convert.ToHexString(hash)[..12]}";
    }

    private static string Scoped(string name) => Scoped(name, OperatingSystem.IsWindows(), Environment.UserName);

    public void Dispose()
    {
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        mutex.Dispose();
    }
}
