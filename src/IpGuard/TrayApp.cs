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
    private readonly List<string> restoreNotice = new();
    private readonly StatusServer statusServer = new();
    private readonly string statusPath;
    private StatusSnapshot? lastSnapshot;
    private int statusPortInUse;
    private readonly ProtectionTrigger protection = new();
    private ProtectForm? protectForm;
    private readonly UpdateChecker updates = new();
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60 * 60 * 1000 };
    private readonly ToolStripMenuItem updateAvailable = new("") { Visible = false };
    private ReleaseInfo? availableRelease;
    private NebulaPanel? panel;
    private PanelState panelState = PanelState.Initial;
    private bool checking;

    public TrayApp()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        dataDir = DataFolder.Current();
        var backups = Path.Combine(dataDir, "backups");
        settingsStore = new JsonStore<Settings>(Path.Combine(dataDir, "settings.json"), backups);
        stateStore = new JsonStore<MonitorState>(Path.Combine(dataDir, "state.json"), backups);
        RegisterErrorHandlers();
        journal = new Journal(Path.Combine(dataDir, "log.txt"));
        statusPath = Path.Combine(dataDir, "status.json");
        settings = settingsStore.Load();
        Sounds.Volume = settings.SoundVolume;
        state = stateStore.Load();
        state.MigrateLegacy();
        ReportRestore("settings.json", settingsStore.LastSource);
        ReportRestore("state.json", stateStore.LastSource);
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
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && !settings.SimpleMenu)
            {
                TogglePanel();
            }
        };
        tray.DoubleClick += (_, _) =>
        {
            if (settings.SimpleMenu)
            {
                ShowHistory();
            }
        };
        tray.Visible = true;
        if (restoreNotice.Count > 0)
        {
            tray.ShowBalloonTip(8000, "IpGuard", string.Join("; ", restoreNotice), ToolTipIcon.Warning);
        }

        pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
        pollTimer.Tick += async (_, _) => await CheckAsync(false, 2, "таймер");
        pollTimer.Start();

        networkTimer.Tick += async (_, _) => await OnNetworkTimerAsync();

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        ApplyStatusServer();
        updateTimer.Tick += async (_, _) => await CheckUpdatesAsync(false);
        updateTimer.Start();
        _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => ui.Post(async _ => await CheckUpdatesAsync(false), null));
        journal.Write($"Запуск, версия {Application.ProductVersion.Split('+')[0]}");
        _ = CheckAsync(true, 5, "запуск");
    }

    protected override void ExitThreadCore()
    {
        journal.Write("Выход");
        PublishStatus(new StatusSnapshot(false, "stopped", "IpGuard закрыт", "Программа не запущена, статус не обновляется", null, null, null, null, Array.Empty<string>(), null, DateTime.UtcNow, ProductVersion()));
        statusServer.Dispose();
        quietTimer.Dispose();
        updateTimer.Dispose();
        protectForm?.Dispose();
        alertForm?.Dispose();
        panel?.Dispose();
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

        var backupItem = BuildBackupMenu();

        var settingsItem = new ToolStripMenuItem("Настройки...");
        settingsItem.Click += async (_, _) => await ShowSettingsAsync();

        var folder = new ToolStripMenuItem("Открыть папку с данными");
        folder.Click += (_, _) => OpenDataFolder();

        updateAvailable.Click += (_, _) => OpenUpdatePage();

        var checkUpdates = new ToolStripMenuItem("Проверить обновления");
        checkUpdates.Click += async (_, _) => await CheckUpdatesAsync(true);

        var version = new ToolStripMenuItem($"Версия {Application.ProductVersion.Split('+')[0]}") { Enabled = false };

        var log = new ToolStripMenuItem("Открыть журнал");
        log.Click += (_, _) => OpenJournal();

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
            backupItem,
            new ToolStripSeparator(),
            folder,
            log,
            new ToolStripSeparator(),
            updateAvailable,
            checkUpdates,
            version,
            exit
        });
        return menu;
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(dataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", dataDir) { UseShellExecute = true });
    }

    private void OpenJournal()
    {
        if (!File.Exists(journal.Path))
        {
            journal.Write("Журнал создан");
        }

        Process.Start(new ProcessStartInfo(journal.Path) { UseShellExecute = true });
    }

    private void TogglePanel()
    {
        if (panel is { IsDisposed: false, Opened: true })
        {
            panel.BeginClose();
            return;
        }

        if (panel is { IsDisposed: false } && (DateTime.UtcNow - panel.LastClosedUtc).TotalMilliseconds < 350)
        {
            return;
        }

        if (panel is null or { IsDisposed: true })
        {
            panel = new NebulaPanel(RunPanelCommand);
            panel.Apply(panelState);
        }

        panel.SetChecking(checking);
        panel.Open(Cursor.Position);
    }

    private async void RunPanelCommand(string id)
    {
        switch (id)
        {
            case "check":
                await CheckAsync(true, 2, "вручную");
                break;
            case "history":
                ShowHistory();
                break;
            case "references":
                ShowReferences();
                break;
            case "settings":
                await ShowSettingsAsync();
                break;
            case "journal":
                OpenJournal();
                break;
            case "folder":
                OpenDataFolder();
                break;
        }
    }

    private void SetChecking(bool value)
    {
        checking = value;
        panel?.SetChecking(value);
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

    private ToolStripMenuItem BuildBackupMenu()
    {
        var root = new ToolStripMenuItem("Резервная копия настроек");

        var export = new ToolStripMenuItem("Сохранить в файл...");
        export.Click += (_, _) => ExportBackup();

        var import = new ToolStripMenuItem("Загрузить из файла...");
        import.Click += async (_, _) => await ImportBackupAsync();

        root.DropDownItems.Add(export);
        root.DropDownItems.Add(import);
        return root;
    }

    private void ExportBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить настройки",
            Filter = "Резервная копия IpGuard (*.json)|*.json",
            FileName = $"ipguard-settings-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        state.MigrateLegacy();
        try
        {
            File.WriteAllText(dialog.FileName, SettingsBackup.Export(settings, state.References), new System.Text.UTF8Encoding(true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Не удалось сохранить файл: {e.Message}", "IpGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        journal.Write($"Настройки сохранены в файл: {dialog.FileName}");
        tray.ShowBalloonTip(4000, "IpGuard", "Настройки и эталоны сохранены в файл", ToolTipIcon.Info);
    }

    private async Task ImportBackupAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Загрузить настройки",
            Filter = "Резервная копия IpGuard (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        string text;
        try
        {
            if (new FileInfo(dialog.FileName).Length > SettingsBackup.MaxBytes)
            {
                MessageBox.Show("Файл слишком большой для резервной копии настроек.", "IpGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Не удалось прочитать файл: {e.Message}", "IpGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var result = SettingsBackup.Import(text);
        if (result.Data is not { } data)
        {
            MessageBox.Show(result.Error, "IpGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(SettingsBackup.Describe(result), "Загрузка настроек", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        settings.CopyFrom(data.Settings);
        state.Trusted.Clear();
        state.References.Clear();
        foreach (var reference in data.References)
        {
            state.AddReference(reference);
        }

        Sounds.Volume = settings.SoundVolume;
        pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
        SaveSettings();
        Persist();
        ApplyStatusServer();
        journal.Write($"Настройки загружены из файла: {dialog.FileName}; эталонов: {data.References.Count}");
        await CheckAsync(true, 1, "импорт");
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
        if (text is null)
        {
            return;
        }

        journal.Write($"Файл {file}: {text}");
        restoreNotice.Add($"{file} {text}");
    }

    private void RegisterErrorHandlers()
    {
        Application.ThreadException += (_, e) => journal.Write(ErrorText.Format("интерфейс", e.Exception));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            journal.Write(ErrorText.Format("фоновая задача", e.Exception));
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => journal.Write(ErrorText.Format("необработанная ошибка", e.ExceptionObject as Exception));
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
                try
                {
                    Autostart.Set(form.AutostartResult);
                }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    journal.Write($"Не удалось изменить автозапуск: {e.Message}");
                    MessageBox.Show("Не удалось изменить автозапуск: Windows не разрешила запись. Остальные настройки сохранены.", "IpGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            ApplyStatusServer();
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

        SetChecking(true);
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
            SetChecking(false);
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

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (!manual && (!settings.UpdateCheckEnabled || !UpdateChecker.ShouldCheck(settings.LastUpdateCheckUtc, DateTime.UtcNow)))
        {
            return;
        }

        var latest = await updates.LatestAsync();
        var current = Versions.Parse(ProductVersion()) ?? new Version(0, 0, 0);
        var outcome = UpdateChecker.Evaluate(latest, current, settings.NotifiedUpdate, manual);
        var show = outcome.Notify && (manual || !quiet.Active);
        if (outcome.Failed)
        {
            journal.Write("Проверка обновлений: не удалось получить ответ");
        }
        else
        {
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            availableRelease = outcome.Available;
            updateAvailable.Visible = outcome.Available is not null;
            if (outcome.Available is { } release)
            {
                updateAvailable.Text = $"Доступна версия {release.Tag}...";
                journal.Write($"Проверка обновлений: доступна {release.Tag}");
                if (show)
                {
                    settings.NotifiedUpdate = release.Tag;
                }
            }
            else
            {
                journal.Write("Проверка обновлений: установлена последняя версия");
            }

            SaveSettings();
        }

        if (show)
        {
            tray.ShowBalloonTip(6000, "IpGuard", outcome.Message, outcome.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
    }

    private void OpenUpdatePage()
    {
        if (availableRelease is null)
        {
            return;
        }

        var url = UpdateChecker.IsSafeUrl(availableRelease.Url) ? availableRelease.Url : UpdateChecker.FallbackPage;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void EvaluateProtection(Verdict verdict)
    {
        var reason = protection.Evaluate(settings, verdict.Status, watcher.CurrentOutage);
        if (reason is null)
        {
            return;
        }

        var programs = settings.ProtectPrograms.ToList();
        var text = reason == ProtectionReason.Alarm
            ? verdict.Title
            : $"Нет связи дольше {settings.ProtectOutageSeconds} с";
        journal.Write($"Защита сработала ({text}): {string.Join(", ", programs)}; время на отмену {settings.ProtectGraceSeconds} с");

        if (settings.ProtectGraceSeconds <= 0)
        {
            CloseProtectedPrograms(programs);
            return;
        }

        if (protectForm is { IsDisposed: false })
        {
            return;
        }

        protectForm = new ProtectForm(
            text,
            programs,
            settings.ProtectGraceSeconds,
            () => CloseProtectedPrograms(programs),
            () => journal.Write("Защита: закрытие отменено"));
        protectForm.Begin();
    }

    private void CloseProtectedPrograms(IReadOnlyList<string> programs)
    {
        var report = ProgramCloser.Close(programs);
        journal.Write($"Защита: {report.Summary}");
        tray.ShowBalloonTip(6000, "IpGuard", $"Защита: {report.Summary}", ToolTipIcon.Warning);
    }

    private static string ProductVersion() => Application.ProductVersion.Split('+')[0];

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
            StatusMapper.WriteFile(statusPath, snapshot);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        statusServer.Update(snapshot);
    }

    private void Show(Verdict verdict, IpInfo? info)
    {
        PublishStatus(StatusMapper.Build(
            verdict,
            info,
            info is null ? Array.Empty<Reference>() : state.ReferencesFor(info.CountryCode),
            monitor.Leak,
            DateTime.UtcNow,
            ProductVersion()));

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

        panelState = PanelState.From(verdict, info, abroad ? reference.Text ?? "" : "");
        panel?.Apply(panelState);

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

        EvaluateProtection(verdict);
    }
}
