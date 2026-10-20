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
    private readonly Func<bool> directEnabled;
    private readonly Func<bool> ipv6Enabled;

    public IpMonitor(
        IIpLookup lookup,
        IpWatcher watcher,
        Action save,
        Func<TimeSpan, Task>? delay = null,
        Func<string>? home = null,
        IIpLookup? direct = null,
        IIpLookup? ipv6 = null,
        Func<bool>? proxyActive = null,
        Func<bool>? directEnabled = null,
        Func<bool>? ipv6Enabled = null)
    {
        this.lookup = lookup;
        this.watcher = watcher;
        this.save = save;
        this.delay = delay ?? Task.Delay;
        this.home = home ?? (() => "RU");
        this.direct = direct;
        this.ipv6 = ipv6;
        this.proxyActive = proxyActive ?? (() => false);
        this.directEnabled = directEnabled ?? (() => true);
        this.ipv6Enabled = ipv6Enabled ?? (() => true);
    }

    public IpInfo? Current { get; private set; }

    public string? Leak => watcher.Leak;

    public IpInfo? DirectInfo { get; private set; }

    public IpInfo? Ipv6Info { get; private set; }

    public bool Proxied { get; private set; }

    public string? Disagreement { get; private set; }

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

        Disagreement = null;
        if (info is null && direct is not null && directEnabled() && proxyActive())
        {
            var viaDirect = await direct.GetAsync();
            if (viaDirect is not null && (viaDirect.CountryCode == home() || watcher.IsMismatch(viaDirect)))
            {
                Current = null;
                Proxied = true;
                DirectInfo = viaDirect;
                Ipv6Info = null;
                var alarm = watcher.EvaluateProxyDown(viaDirect, force);
                save();
                return (alarm, viaDirect);
            }
        }

        if (info is not null && watcher.IsMismatch(info))
        {
            var second = await lookup.GetAsync();
            if (second is not null && second.Ip != info.Ip)
            {
                Disagreement = $"{info.Ip} и {second.Ip}";
                if (watcher.IsReference(second))
                {
                    info = second;
                }
            }
        }

        Current = info;
        string? leak = null;
        DirectInfo = null;
        Ipv6Info = null;
        Proxied = false;
        if (info is not null && info.CountryCode != home())
        {
            Proxied = direct is not null && directEnabled() && proxyActive();
            DirectInfo = Proxied ? await direct!.GetAsync() : null;
            Ipv6Info = ipv6 is not null && ipv6Enabled() ? await ipv6.GetAsync() : null;
            leak = LeakDetector.Find(info, DirectInfo, Ipv6Info, home());
        }

        var verdict = watcher.Evaluate(info, force, leak);
        save();
        return (verdict, info);
    }
}
