using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace IpGuard;

public sealed class TrayApp : ApplicationContext
{
    private const int NetworkBurstChecks = 3;

    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer pollTimer = new();
    private readonly System.Windows.Forms.Timer networkTimer = new() { Interval = 3000 };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SynchronizationContext ui;
    private readonly string dataDir;
    private readonly JsonStore<Settings> settingsStore;
    private readonly JsonStore<MonitorState> stateStore;
    private readonly Settings settings;
    private readonly MonitorState state;
    private readonly IpWatcher watcher;
    private readonly IpMonitor monitor;
    private readonly IpLookup primary = new();
    private readonly Journal journal;
    private string? lastRecord;
    private readonly ToolStripMenuItem header = new("Проверка...") { Enabled = false };
    private readonly ToolStripMenuItem reference = new("") { Enabled = false, Visible = false };
    private readonly ToolStripMenuItem directRoute = new("") { Enabled = false, Visible = false };
    private readonly ToolStripMenuItem ipv6Route = new("") { Enabled = false, Visible = false };
    private HistoryForm? historyForm;
    private AlertForm? alertForm;
    private readonly QuietMode quiet = new();
    private readonly System.Windows.Forms.Timer quietTimer = new();
    private int networkBurst;
    private bool rerun;
    private bool settingsOpen;
    private bool referencesOpen;

    public TrayApp()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IpGuard");
        settingsStore = new JsonStore<Settings>(Path.Combine(dataDir, "settings.json"));
        stateStore = new JsonStore<MonitorState>(Path.Combine(dataDir, "state.json"));
        journal = new Journal(Path.Combine(dataDir, "log.txt"));
        settings = settingsStore.Load();
        Sounds.Volume = settings.SoundVolume;
        state = stateStore.Load();
        state.MigrateLegacy();
        watcher = new IpWatcher(
            state,
            () => settings.HomeCountryCode,
            outageThreshold: () => TimeSpan.FromSeconds(Math.Max(0, settings.OutageSeconds)));
        monitor = new IpMonitor(
            primary,
            watcher,
            Persist,
            home: () => settings.HomeCountryCode,
            direct: IpLookup.Direct(),
            ipv6: new Ipv6Lookup(),
            proxyActive: IpLookup.SystemProxyActive,
            directEnabled: () => settings.CheckDirectRoute,
            ipv6Enabled: () => settings.CheckIpv6);

        tray.Icon = TrayIcons.For(Status.Unavailable);
        tray.Text = "IpGuard";
        tray.ContextMenuStrip = BuildMenu();
        tray.DoubleClick += (_, _) => ShowHistory();
        tray.Visible = true;

        pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
        pollTimer.Tick += async (_, _) => await CheckAsync(false, 2, "таймер");
        pollTimer.Start();

        networkTimer.Tick += async (_, _) => await OnNetworkTimerAsync();

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        journal.Write($"Запуск, версия {Application.ProductVersion.Split('+')[0]}");
        _ = CheckAsync(true, 5, "запуск");
    }

    protected override void ExitThreadCore()
    {
        journal.Write("Выход");
        quietTimer.Dispose();
        alertForm?.Dispose();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        pollTimer.Stop();
        networkTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var check = new ToolStripMenuItem("Проверить сейчас");
        check.Click += async (_, _) => await CheckAsync(true, 2, "вручную");

        var trust = new ToolStripMenuItem("Принять текущий IP как эталон");
        trust.Click += async (_, _) => await AcceptCurrentAsReferenceAsync();

        var home = new ToolStripMenuItem("Считать текущую страну домашней");
        home.Click += async (_, _) =>
        {
            var current = monitor.Current;
            if (current is not null)
            {
                settings.HomeCountryCode = current.CountryCode;
                SaveSettings();
                journal.Write($"Домашняя страна: {current.CountryCode}");
                await CheckAsync(true, 1, "домашняя страна");
            }
        };

        var history = new ToolStripMenuItem("История...");
        history.Click += (_, _) => ShowHistory();

        var quietMenu = BuildQuietMenu();

        var referencesItem = new ToolStripMenuItem("Эталоны...");
        referencesItem.Click += (_, _) => ShowReferences();

        var settingsItem = new ToolStripMenuItem("Настройки...");
        settingsItem.Click += async (_, _) => await ShowSettingsAsync();

        var folder = new ToolStripMenuItem("Открыть папку с данными");
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(dataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", dataDir) { UseShellExecute = true });
        };

        var version = new ToolStripMenuItem($"Версия {Application.ProductVersion.Split('+')[0]}") { Enabled = false };

        var log = new ToolStripMenuItem("Открыть журнал");
        log.Click += (_, _) =>
        {
            if (!File.Exists(journal.Path))
            {
                journal.Write("Журнал создан");
            }

            Process.Start(new ProcessStartInfo(journal.Path) { UseShellExecute = true });
        };

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            header,
            directRoute,
            ipv6Route,
            reference,
            new ToolStripSeparator(),
            check,
            trust,
            home,
            history,
            quietMenu,
            referencesItem,
            settingsItem,
            new ToolStripSeparator(),
            folder,
            log,
            new ToolStripSeparator(),
            version,
            exit
        });
        return menu;
    }

    private void SaveSettings()
    {
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private ToolStripMenuItem BuildQuietMenu()
    {
        var root = new ToolStripMenuItem(quiet.Title);
        var stop = new ToolStripMenuItem("Выключить") { Enabled = false };

        void Apply(TimeSpan? duration)
        {
            if (duration is { } span)
            {
                quiet.Start(span);
                quietTimer.Interval = (int)Math.Min(int.MaxValue, span.TotalMilliseconds + 500);
                quietTimer.Stop();
                quietTimer.Start();
                journal.Write($"Не беспокоить: {span.TotalMinutes:0} мин");
            }
            else
            {
                quiet.Stop();
                quietTimer.Stop();
                journal.Write("Не беспокоить: выключено");
            }

            RefreshQuietMenu(root, stop);
        }

        foreach (var minutes in new[] { 15, 30, 60 })
        {
            var item = new ToolStripMenuItem($"{minutes} минут");
            item.Click += (_, _) => Apply(TimeSpan.FromMinutes(minutes));
            root.DropDownItems.Add(item);
        }

        root.DropDownItems.Add(new ToolStripSeparator());
        stop.Click += (_, _) => Apply(null);
        root.DropDownItems.Add(stop);

        quietTimer.Tick += (_, _) =>
        {
            quietTimer.Stop();
            journal.Write("Не беспокоить: время вышло");
            RefreshQuietMenu(root, stop);
        };
        root.DropDownOpening += (_, _) => RefreshQuietMenu(root, stop);
        return root;
    }

    private void RefreshQuietMenu(ToolStripMenuItem root, ToolStripMenuItem stop)
    {
        root.Text = quiet.Title;
        stop.Enabled = quiet.Active;
    }

    private void ShowReferences()
    {
        if (referencesOpen)
        {
            return;
        }

        referencesOpen = true;
        try
        {
            using var form = new ReferencesForm(
                state,
                () => monitor.Current,
                () => settings.HomeCountryCode,
                () =>
                {
                    Persist();
                    journal.Write("Список эталонов изменён");
                    _ = CheckAsync(true, 1, "эталоны");
                });
            form.ShowDialog();
        }
        finally
        {
            referencesOpen = false;
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (settingsOpen)
        {
            return;
        }

        settingsOpen = true;
        try
        {
            using var form = new SettingsForm(settings, Autostart.IsEnabled());
            if (form.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            settings.CopyFrom(form.Result);
            Sounds.Volume = settings.SoundVolume;
            SaveSettings();
            pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
            if (form.AutostartResult != Autostart.IsEnabled())
            {
                Autostart.Set(form.AutostartResult);
            }

            journal.Write("Настройки сохранены");
            await CheckAsync(true, 1, "настройки");
        }
        finally
        {
            settingsOpen = false;
        }
    }

    private async Task AcceptCurrentAsReferenceAsync()
    {
        var current = monitor.Current;
        if (current is not null && current.CountryCode != settings.HomeCountryCode)
        {
            var added = watcher.Trust(current);
            journal.Write(added
                ? $"Эталон добавлен для {current.CountryCode}: {current.Ip}"
                : $"Эталон уже был для {current.CountryCode}: {current.Ip}");
            await CheckAsync(true, 1, "новый эталон");
        }
    }

    private void ShowAlert(Verdict verdict)
    {
        if (alertForm is null or { IsDisposed: true })
        {
            alertForm = new AlertForm(() => settings.SoundEnabled && !quiet.Active);
        }

        var key = Sounds.KeyFor(verdict.Status);
        var canAccept = verdict.Status == Status.Changed;
        alertForm.Raise(verdict, key, key is null ? 1 : settings.SoundVariant(key), canAccept, async () => await AcceptCurrentAsReferenceAsync());
        journal.Write($"Окно тревоги: {verdict.Title}");
    }

    private void ShowHistory()
    {
        if (historyForm is { IsDisposed: false })
        {
            if (historyForm.WindowState == FormWindowState.Minimized)
            {
                historyForm.WindowState = FormWindowState.Normal;
            }

            historyForm.Activate();
            return;
        }

        historyForm = new HistoryForm(
            state,
            () => settings.HomeCountryCode,
            () =>
            {
                Persist();
                journal.Write("История очищена");
            });
        historyForm.FormClosed += (_, _) => historyForm = null;
        historyForm.Show();
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        ui.Post(_ =>
        {
            networkBurst = NetworkBurstChecks;
            networkTimer.Stop();
            networkTimer.Interval = 3000;
            networkTimer.Start();
        }, null);
    }

    private async Task OnNetworkTimerAsync()
    {
        networkTimer.Stop();
        if (networkBurst <= 0)
        {
            return;
        }

        networkBurst--;
        if (networkBurst > 0)
        {
            networkTimer.Interval = 7000;
            networkTimer.Start();
        }

        await CheckAsync(false, 2, "смена сети");
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        ui.Post(async _ =>
        {
            await Task.Delay(6000);
            await CheckAsync(true, 6, "пробуждение");
        }, null);
    }

    private async Task CheckAsync(bool force, int attempts, string reason)
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
                var (verdict, info) = await monitor.CheckAsync(force, attempts);
                Record(reason, verdict, info);
                Show(verdict, info);
                force = false;
                reason = "повтор";
                attempts = Math.Min(attempts, 2);
            }
            while (rerun);
        }
        finally
        {
            gate.Release();
        }
    }

    private void Record(string reason, Verdict verdict, IpInfo? info)
    {
        var place = info is null ? "адрес не определён" : $"{info.Ip} {info.CountryCode}";
        var proxy = SystemProxy.Current() is null ? "без прокси" : "через прокси";
        var paused = primary.PausedSources.Count == 0 ? "" : $" | на паузе: {string.Join(", ", primary.PausedSources)}";
        var summary = $"{Labels.For(verdict.Status)} | {place} | {proxy}{paused}";
        if (reason == "таймер" && !verdict.Notify && summary == lastRecord)
        {
            return;
        }

        lastRecord = summary;
        var leak = monitor.Leak is null ? "" : $" | {monitor.Leak}";
        var alert = verdict.Notify ? " | уведомление" : "";
        if (watcher.LastOutage is { } outage)
        {
            alert += $" | связи не было {IpWatcher.FormatDuration(outage)}";
        }

        if (monitor.Disagreement is { } disagreement)
        {
            alert += $" | источники расходятся: {disagreement}";
        }

        var provider = string.IsNullOrEmpty(info?.Provider) ? "" : $" | {info.Provider}";
        journal.Write($"{reason}: {summary}{provider}{leak}{alert}");
    }

    private void ShowRoutes(IpInfo? info)
    {
        var abroad = info is not null && info.CountryCode != settings.HomeCountryCode;
        directRoute.Visible = abroad && monitor.Proxied;
        directRoute.Text = monitor.DirectInfo is { } direct
            ? $"Без прокси: {direct.Ip} · {direct.CountryCode}"
            : "Без прокси: не определён";

        ipv6Route.Visible = abroad && settings.CheckIpv6;
        ipv6Route.Text = monitor.Ipv6Info is { } v6
            ? $"IPv6: {v6.Ip}" + (v6.CountryCode.Length == 2 ? $" · {v6.CountryCode}" : "")
            : "IPv6: нет";
    }

    private void Persist()
    {
        try
        {
            stateStore.Save(state);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Show(Verdict verdict, IpInfo? info)
    {
        if (historyForm is { IsDisposed: false })
        {
            historyForm.Reload();
        }

        tray.Icon = TrayIcons.For(verdict.Status);
        var tip = info is null ? "IpGuard: нет связи" : $"{info.CountryCode} {info.Ip} — {Labels.For(verdict.Status)}";
        tray.Text = tip.Length > 63 ? tip[..63] : tip;
        header.Text = info is null ? "Нет связи" : $"{info.Ip} · {info.Country} · {info.Provider}";
        ShowRoutes(info);
        var abroad = info is not null && info.CountryCode != settings.HomeCountryCode;
        reference.Visible = abroad;
        if (abroad)
        {
            var references = state.ReferencesFor(info!.CountryCode);
            var text = references.Count == 0
                ? $"Эталон для {info.CountryCode} не задан"
                : $"{(references.Count == 1 ? "Эталон" : "Эталоны")} для {info.CountryCode}: {IpWatcher.DescribeReferences(references)}";
            reference.Text = text.Length > 70 ? text[..69] + "…" : text;
        }

        var alarm = verdict.Status is Status.Changed or Status.Leak;
        var plan = NotificationPolicy.Decide(verdict, settings.AlertWindow, settings.SoundEnabled, quiet.Active);
        if (plan.Window)
        {
            ShowAlert(verdict);
        }
        else if (!(alarm && settings.AlertWindow) && alertForm is { IsDisposed: false, Active: true })
        {
            alertForm.Resolve($"{verdict.Title}. {verdict.Message}");
        }

        if (plan.Balloon)
        {
            var icon = verdict.Status is Status.Changed or Status.Leak or Status.VpnOff or Status.Unavailable
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
            tray.ShowBalloonTip(6000, verdict.Title, verdict.Message, icon);
        }

        if (plan.Sound && Sounds.KeyFor(verdict.Status) is { } key)
        {
            Sounds.Play(key, settings.SoundVariant(key));
        }
    }
}
