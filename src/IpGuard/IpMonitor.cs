namespace IpGuard;

public sealed class IpMonitor
{
    private readonly IIpLookup lookup;
    private readonly IpWatcher watcher;
    private readonly Action save;
    private readonly Func<TimeSpan, Task> delay;

    public IpMonitor(IIpLookup lookup, IpWatcher watcher, Action save, Func<TimeSpan, Task>? delay = null)
    {
        this.lookup = lookup;
        this.watcher = watcher;
        this.save = save;
        this.delay = delay ?? Task.Delay;
    }

    public IpInfo? Current { get; private set; }

    public async Task<(Verdict Verdict, IpInfo? Info)> CheckAsync(bool force, int attempts)
    {
        IpInfo? info = null;
        for (var i = 0; i < attempts && info is null; i++)
        {
            info = await lookup.GetAsync();
            if (info is null && i < attempts - 1)
            {
                await delay(TimeSpan.FromSeconds(4));
            }
        }

        Current = info;
        var verdict = watcher.Evaluate(info, force);
        save();
        return (verdict, info);
    }
}
