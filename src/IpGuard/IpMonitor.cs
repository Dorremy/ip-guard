namespace IpGuard;

public sealed class IpMonitor
{
    private readonly IIpLookup lookup;
    private readonly IpWatcher watcher;
    private readonly Action save;
    private readonly Func<TimeSpan, Task> delay;
    private readonly Func<string> home;
    private readonly IIpLookup? direct;
    private readonly IIpLookup? ipv6;
    private readonly Func<bool> proxyActive;
    private string? lastLeak;

    public IpMonitor(
        IIpLookup lookup,
        IpWatcher watcher,
        Action save,
        Func<TimeSpan, Task>? delay = null,
        Func<string>? home = null,
        IIpLookup? direct = null,
        IIpLookup? ipv6 = null,
        Func<bool>? proxyActive = null)
    {
        this.lookup = lookup;
        this.watcher = watcher;
        this.save = save;
        this.delay = delay ?? Task.Delay;
        this.home = home ?? (() => "RU");
        this.direct = direct;
        this.ipv6 = ipv6;
        this.proxyActive = proxyActive ?? (() => false);
    }

    public IpInfo? Current { get; private set; }

    public string? Leak => lastLeak;

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

        if (info is not null && verdict.Status is Status.Unchanged or Status.NoReference or Status.Changed)
        {
            var directInfo = direct is not null && proxyActive() ? await direct.GetAsync() : null;
            var ipv6Info = ipv6 is not null ? await ipv6.GetAsync() : null;
            var leak = LeakDetector.Find(info, directInfo, ipv6Info, home());
            if (leak is not null && verdict.Status != Status.Changed)
            {
                verdict = new Verdict(Status.Leak, "Утечка мимо VPN!", leak, force || leak != lastLeak);
            }
            else if (leak is null && lastLeak is not null)
            {
                verdict = verdict with { Notify = true };
            }

            lastLeak = leak;
        }
        else
        {
            lastLeak = null;
        }

        save();
        return (verdict, info);
    }
}
