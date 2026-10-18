using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace IpGuard;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer pollTimer = new();
    private readonly System.Windows.Forms.Timer networkTimer = new() { Interval = 4000 };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SynchronizationContext ui;
    private readonly string dataDir;
    private readonly JsonStore<Settings> settingsStore;
    private readonly JsonStore<MonitorState> stateStore;
    private readonly Settings settings;
    private readonly MonitorState state;
    private readonly IpWatcher watcher;
    private readonly IpLookup lookup = new();
    private readonly ToolStripMenuItem header = new("Проверка...") { Enabled = false };
    private IpInfo? current;

    public TrayApp()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IpGuard");
        settingsStore = new JsonStore<Settings>(Path.Combine(dataDir, "settings.json"));
        stateStore = new JsonStore<MonitorState>(Path.Combine(dataDir, "state.json"));
        settings = settingsStore.Load();
        state = stateStore.Load();
        watcher = new IpWatcher(state, () => settings.HomeCountryCode);

        tray.Icon = TrayIcons.For(Status.Unavailable);
        tray.Text = "IpGuard";
        tray.ContextMenuStrip = BuildMenu();
        tray.DoubleClick += (_, _) => ShowHistory();
        tray.Visible = true;

        pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
        pollTimer.Tick += async (_, _) => await CheckAsync(false, 2);
        pollTimer.Start();

        networkTimer.Tick += async (_, _) =>
        {
            networkTimer.Stop();
            await CheckAsync(false, 3);
        };

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
            if (current is not null && current.CountryCode != settings.HomeCountryCode)
            {
                watcher.Trust(current);
                await CheckAsync(true, 1);
            }
        };

        var home = new ToolStripMenuItem("Считать текущую страну домашней");
        home.Click += async (_, _) =>
        {
            if (current is not null)
            {
                settings.HomeCountryCode = current.CountryCode;
                settingsStore.Save(settings);
                await CheckAsync(true, 1);
            }
        };

        var history = new ToolStripMenuItem("История...");
        history.Click += (_, _) => ShowHistory();

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

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            header,
            new ToolStripSeparator(),
            check,
            trust,
            home,
            history,
            new ToolStripSeparator(),
            autostart,
            folder,
            new ToolStripSeparator(),
            exit
        });
        return menu;
    }

    private void ShowHistory()
    {
        using var form = new HistoryForm(state, settings.HomeCountryCode);
        form.ShowDialog();
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        ui.Post(_ =>
        {
            networkTimer.Stop();
            networkTimer.Start();
        }, null);
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
            return;
        }

        try
        {
            IpInfo? info = null;
            for (var i = 0; i < attempts && info is null; i++)
            {
                info = await lookup.GetAsync();
                if (info is null && i < attempts - 1)
                {
                    await Task.Delay(4000);
                }
            }

            current = info;
            var verdict = watcher.Evaluate(info, force);
            Persist();
            Show(verdict, info);
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

        if (verdict.Notify)
        {
            var icon = verdict.Status is Status.Changed or Status.VpnOff or Status.Unavailable
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
            tray.ShowBalloonTip(6000, verdict.Title, verdict.Message, icon);
        }
    }
}
