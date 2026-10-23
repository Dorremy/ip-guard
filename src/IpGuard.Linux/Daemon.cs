namespace IpGuard;

public sealed record DaemonOptions(
    DataPaths Paths,
    IIpLookup Primary,
    Func<IReadOnlyList<string>> PausedSources,
    IIpLookup? Direct,
    IIpLookup? Ipv6,
    Func<bool> ProxyActive,
    Func<Action<string>, INotifier> NotifierFactory,
    string Version,
    Func<DateTime>? UtcNow = null,
    Func<TimeSpan, Task>? Delay = null);

public sealed class Daemon : IDisposable
{
    private const int NetworkBurstChecks = 3;

    private readonly DataPaths paths;
    private readonly Func<DateTime> utcNow;
    private readonly string version;
    private readonly Func<IReadOnlyList<string>> pausedSources;
    private readonly Func<bool> proxyActive;
    private readonly JsonStore<Settings> settingsStore;
    private readonly JsonStore<MonitorState> stateStore;
    private readonly FileStamp settingsStamp;
    private readonly FileStamp stateStamp;
    private readonly Settings settings;
    private readonly MonitorState state;
    private readonly IpWatcher watcher;
    private readonly IpMonitor monitor;
    private readonly Journal journal;
    private readonly INotifier notifier;
    private readonly StatusServer statusServer = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Timer pollTimer;
    private readonly Timer networkTimer;
    private string? lastRecord;
    private StatusSnapshot? lastSnapshot;
    private int statusPortInUse;
    private int networkBurst;
    private volatile bool rerun;
    private volatile bool stopping;

    public Daemon(DaemonOptions options)
    {
        paths = options.Paths;
        utcNow = options.UtcNow ?? (() => DateTime.UtcNow);
        version = options.Version;
        pausedSources = options.PausedSources;
        proxyActive = options.ProxyActive;
        journal = new Journal(paths.Log);
        notifier = options.NotifierFactory(journal.Write);
        settingsStore = paths.SettingsStore();
        stateStore = paths.StateStore();
        settingsStamp = new FileStamp(paths.Settings);
        stateStamp = new FileStamp(paths.State);
        settings = settingsStore.Load();
        ReportRestore("settings.json", settingsStore.LastSource);
        state = stateStore.Load();
        state.MigrateLegacy();
        ReportRestore("state.json", stateStore.LastSource);
        settingsStamp.Mark();
        stateStamp.Mark();
        watcher = new IpWatcher(
            state,
            () => settings.HomeCountryCode,
            outageThreshold: () => TimeSpan.FromSeconds(Math.Max(0, settings.OutageSeconds))) { ReferenceHint = "Чтобы запомнить этот IP, выполните: ipguard trust" };
        monitor = new IpMonitor(
            options.Primary,
            watcher,
            Persist,
            delay: options.Delay,
            home: () => settings.HomeCountryCode,
            direct: options.Direct,
            ipv6: options.Ipv6,
            proxyActive: options.ProxyActive,
            directEnabled: () => settings.CheckDirectRoute,
            ipv6Enabled: () => settings.CheckIpv6);
        pollTimer = new Timer(_ => Fire(() => CheckAsync(false, 2, "таймер")));
        networkTimer = new Timer(_ => Fire(OnNetworkTimerAsync));
    }

    public Settings Settings => settings;

    public MonitorState State => state;

    public StatusSnapshot? LastSnapshot => lastSnapshot;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(15, settings.IntervalSeconds));

    public Task Start()
    {
        ApplyInterval();
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        ApplyStatusServer();
        journal.Write($"Запуск, версия {version}");
        return CheckAsync(true, 5, "запуск");
    }

    public async Task StopAsync()
    {
        stopping = true;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        networkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        var idle = await gate.WaitAsync(TimeSpan.FromSeconds(5));
        journal.Write("Выход");
        PublishStatus(new StatusSnapshot(false, "stopped", "IpGuard закрыт", "Программа не запущена, статус не обновляется", null, null, null, null, Array.Empty<string>(), null, utcNow(), version));
        statusServer.Dispose();
        if (idle)
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        pollTimer.Dispose();
        networkTimer.Dispose();
        statusServer.Dispose();
    }

    public async Task CheckAsync(bool force, int attempts, string reason)
    {
        if (!await gate.WaitAsync(0))
        {
            rerun = true;
            return;
        }

        try
        {
            do
            {
                rerun = false;
                ReloadIfChanged();
                var (verdict, info) = await monitor.CheckAsync(force, attempts);
                if (stopping)
                {
                    break;
                }

                Record(reason, verdict, info);
                await ShowAsync(verdict, info);
                force = false;
                reason = "повтор";
                attempts = Math.Min(attempts, 2);
            }
            while (rerun && !stopping);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool ReloadIfChanged()
    {
        var changed = false;
        if (settingsStamp.Changed())
        {
            var loaded = settingsStore.Load();
            settingsStamp.Mark();
            if (settingsStore.LastSource == LoadSource.Default)
            {
                journal.Write("Файл settings.json изменён снаружи и не читается, оставлены прежние настройки");
            }
            else
            {
                settings.CopyFrom(loaded);
                journal.Write("Файл settings.json изменён снаружи, настройки перечитаны");
                ApplyInterval();
                ApplyStatusServer();
                changed = true;
            }
        }

        if (stateStamp.Changed())
        {
            var loaded = stateStore.Load();
            stateStamp.Mark();
            if (stateStore.LastSource == LoadSource.Default)
            {
                journal.Write("Файл state.json изменён снаружи и не читается, оставлено прежнее состояние");
            }
            else
            {
                StateSync.Replace(state, loaded);
                journal.Write("Файл state.json изменён снаружи, состояние перечитано");
                changed = true;
            }
        }

        return changed;
    }

    private void ApplyInterval()
    {
        var interval = PollInterval;
        pollTimer.Change(interval, interval);
    }

    private void Persist()
    {
        try
        {
            if (stateStamp.Changed() && stateStore.Load() is { } external && stateStore.LastSource != LoadSource.Default)
            {
                if (StateSync.MergeReferences(state, external))
                {
                    journal.Write("Эталоны, добавленные снаружи, сохранены");
                }
            }

            stateStore.Save(state);
            stateStamp.Mark();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Fire(Func<Task> action) => _ = RunSafely(action);

    private async Task RunSafely(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            journal.Write(ErrorText.Format("проверка", e));
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref networkBurst, NetworkBurstChecks);
        networkTimer.Change(3000, Timeout.Infinite);
    }

    private async Task OnNetworkTimerAsync()
    {
        if (Volatile.Read(ref networkBurst) <= 0)
        {
            return;
        }

        if (Interlocked.Decrement(ref networkBurst) > 0)
        {
            networkTimer.Change(7000, Timeout.Infinite);
        }

        await CheckAsync(false, 2, "смена сети");
    }

    private void Record(string reason, Verdict verdict, IpInfo? info)
    {
        var summary = CheckRecord.Summary(verdict, info, proxyActive(), pausedSources());
        if (reason == "таймер" && !verdict.Notify && summary == lastRecord)
        {
            return;
        }

        lastRecord = summary;
        journal.Write(CheckRecord.Line(reason, summary, verdict, info, monitor.Leak, watcher.LastOutage, monitor.Disagreement));
    }

    private void ReportRestore(string file, LoadSource source)
    {
        var text = source switch
        {
            LoadSource.PreviousVersion => "восстановлен из предыдущей версии",
            LoadSource.Snapshot => "восстановлен из резервной копии",
            LoadSource.Default => "не удалось прочитать, начато с чистых значений",
            _ => null
        };
        if (text is not null)
        {
            journal.Write($"Файл {file}: {text}");
        }
    }

    private void ApplyStatusServer()
    {
        if (!settings.StatusServerEnabled)
        {
            if (statusServer.Running)
            {
                statusServer.Stop();
                statusPortInUse = 0;
                journal.Write("Локальный адрес статуса выключен");
            }

            return;
        }

        if (statusServer.Running && statusPortInUse == settings.StatusPort)
        {
            return;
        }

        if (statusServer.Start(settings.StatusPort, out var error))
        {
            statusPortInUse = settings.StatusPort;
            if (lastSnapshot is not null)
            {
                statusServer.Update(lastSnapshot);
            }

            journal.Write($"Локальный адрес статуса: http://127.0.0.1:{settings.StatusPort}/status");
        }
        else
        {
            statusPortInUse = 0;
            journal.Write($"Не удалось открыть порт {settings.StatusPort} для статуса: {error}");
        }
    }

    private void PublishStatus(StatusSnapshot snapshot)
    {
        lastSnapshot = snapshot;
        try
        {
            StatusMapper.WriteFile(paths.Status, snapshot);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        statusServer.Update(snapshot);
    }

    private async Task ShowAsync(Verdict verdict, IpInfo? info)
    {
        PublishStatus(StatusMapper.Build(
            verdict,
            info,
            info is null ? Array.Empty<Reference>() : state.ReferencesFor(info.CountryCode),
            monitor.Leak,
            utcNow(),
            version));

        var plan = NotificationPolicy.Decide(verdict, settings.AlertWindow, false, false);
        if (plan.Window || plan.Balloon)
        {
            await notifier.SendAsync(verdict.Title, verdict.Message, plan.Window);
        }
    }
}
