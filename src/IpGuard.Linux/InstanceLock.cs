namespace IpGuard;

public sealed class InstanceLock : IDisposable
{
    private readonly Mutex mutex;

    private InstanceLock(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static InstanceLock? TryAcquire(string name)
    {
        var mutex = new Mutex(true, name, out var created);
        if (created)
        {
            return new InstanceLock(mutex);
        }

        mutex.Dispose();
        return null;
    }

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
