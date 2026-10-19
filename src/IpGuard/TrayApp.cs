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
    private readonly ToolStripMenuItem header = new("Проверка...") { Enabled = false };
    private readonly ToolStripMenuItem reference = new("") { Enabled = false, Visible = false };
    private HistoryForm? historyForm;
    private int networkBurst;
    private bool rerun;

    public TrayApp()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IpGuard");
        settingsStore = new JsonStore<Settings>(Path.Combine(dataDir, "settings.json"));
        stateStore = new JsonStore<MonitorState>(Path.Combine(dataDir, "state.json"));
        settings = settingsStore.Load();
        state = stateStore.Load();
        watcher = new IpWatcher(state, () => settings.HomeCountryCode);
        monitor = new IpMonitor(
            new IpLookup(),
            watcher,
            Persist,
            home: () => settings.HomeCountryCode,
            direct: IpLookup.Direct(),
            ipv6: new Ipv6Lookup(),
            proxyActive: IpLookup.SystemProxyActive);

        tray.Icon = TrayIcons.For(Status.Unavailable);
        tray.Text = "IpGuard";
        tray.ContextMenuStrip = BuildMenu();
        tray.DoubleClick += (_, _) => ShowHistory();
        tray.Visible = true;

        pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
        pollTimer.Tick += async (_, _) => await CheckAsync(false, 2);
        pollTimer.Start();

        networkTimer.Tick += async (_, _) => await OnNetworkTimerAsync();

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _ = CheckAsync(true, 5);
    }

    protected override void ExitThreadCore()
    {
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
        check.Click += async (_, _) => await CheckAsync(true, 2);

        var trust = new ToolStripMenuItem("Принять текущий IP как эталон");
        trust.Click += async (_, _) =>
        {
            var current = monitor.Current;
            if (current is not null && current.CountryCode != settings.HomeCountryCode)
            {
                watcher.Trust(current);
                await CheckAsync(true, 1);
            }
        };

        var home = new ToolStripMenuItem("Считать текущую страну домашней");
        home.Click += async (_, _) =>
        {
            var current = monitor.Current;
            if (current is not null)
            {
                settings.HomeCountryCode = current.CountryCode;
                SaveSettings();
                await CheckAsync(true, 1);
            }
        };

        var history = new ToolStripMenuItem("История...");
        history.Click += (_, _) => ShowHistory();

        var sounds = BuildSoundMenu();

        var autostart = new ToolStripMenuItem("Запускать вместе с Windows")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled()
        };
        autostart.CheckedChanged += (_, _) => Autostart.Set(autostart.Checked);

        var folder = new ToolStripMenuItem("Открыть папку с данными");
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(dataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", dataDir) { UseShellExecute = true });
        };

        var version = new ToolStripMenuItem($"Версия {Application.ProductVersion.Split('+')[0]}") { Enabled = false };

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            header,
            reference,
            new ToolStripSeparator(),
            check,
            trust,
            home,
            history,
            sounds,
            new ToolStripSeparator(),
            autostart,
            folder,
            new ToolStripSeparator(),
            version,
            exit
        });
        return menu;
    }

    private ToolStripMenuItem BuildSoundMenu()
    {
        var root = new ToolStripMenuItem("Звуки");
        var done = new ToolStripMenuItem("Готово");
        KeepOpenOnChoice(root.DropDown, item => item != done);

        var enabled = new ToolStripMenuItem("Включены")
        {
            CheckOnClick = true,
            Checked = settings.SoundEnabled
        };
        enabled.CheckedChanged += (_, _) =>
        {
            settings.SoundEnabled = enabled.Checked;
            SaveSettings();
        };
        root.DropDownItems.Add(enabled);
        root.DropDownItems.Add(new ToolStripSeparator());

        foreach (var (key, title, names) in Sounds.Kinds)
        {
            var kind = new ToolStripMenuItem(title);
            KeepOpenOnChoice(kind.DropDown, _ => true);
            for (var variant = 1; variant <= Sounds.Variants; variant++)
            {
                var number = variant;
                var item = new ToolStripMenuItem(names[number - 1]) { Checked = settings.SoundVariant(key) == number };
                item.Click += (_, _) =>
                {
                    settings.Sounds[key] = number;
                    SaveSettings();
                    foreach (ToolStripMenuItem sibling in kind.DropDownItems)
                    {
                        sibling.Checked = sibling == item;
                    }

                    Sounds.Play(key, number);
                };
                kind.DropDownItems.Add(item);
            }

            root.DropDownItems.Add(kind);
        }

        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(done);
        return root;
    }

    private static void KeepOpenOnChoice(ToolStripDropDown dropDown, Func<ToolStripItem, bool> keepOpen)
    {
        dropDown.Closing += (_, e) =>
        {
            var selected = dropDown.Items.Cast<ToolStripItem>().FirstOrDefault(item => item.Selected);
            e.Cancel = e.CloseReason == ToolStripDropDownCloseReason.ItemClicked
                && selected is not null
                && keepOpen(selected);
        };
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

        historyForm = new HistoryForm(state, settings.HomeCountryCode);
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

        await CheckAsync(false, 2);
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
            await CheckAsync(true, 6);
        }, null);
    }

    private async Task CheckAsync(bool force, int attempts)
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
                Show(verdict, info);
                force = false;
                attempts = Math.Min(attempts, 2);
            }
            while (rerun);
        }
        finally
        {
            gate.Release();
        }
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
        tray.Icon = TrayIcons.For(verdict.Status);
        var tip = info is null ? "IpGuard: нет связи" : $"{info.CountryCode} {info.Ip} — {Labels.For(verdict.Status)}";
        tray.Text = tip.Length > 63 ? tip[..63] : tip;
        header.Text = info is null ? "Нет связи" : $"{info.Ip} · {info.Country} · {info.Provider}";
        var abroad = info is not null && info.CountryCode != settings.HomeCountryCode;
        reference.Visible = abroad;
        if (abroad)
        {
            reference.Text = state.Trusted.TryGetValue(info!.CountryCode, out var trusted)
                ? $"Эталон для {info.CountryCode}: {trusted}"
                : $"Эталон для {info.CountryCode} не задан";
        }

        if (verdict.Notify)
        {
            var icon = verdict.Status is Status.Changed or Status.Leak or Status.VpnOff or Status.Unavailable
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
            tray.ShowBalloonTip(6000, verdict.Title, verdict.Message, icon);
            if (settings.SoundEnabled && Sounds.KeyFor(verdict.Status) is { } key)
            {
                Sounds.Play(key, settings.SoundVariant(key));
            }
        }
    }
}
