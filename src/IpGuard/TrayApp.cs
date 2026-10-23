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
    private ToastWindow? toast;
    private readonly System.Windows.Forms.Timer alertSound = new() { Interval = 4000 };
    private string? alertKey;
    private int alertVariant = 1;
    private readonly QuietMode quiet = new();
    private readonly System.Windows.Forms.Timer quietTimer = new();
    private int networkBurst;
    private bool rerun;
    private readonly List<string> restoreNotice = new();
    private readonly StatusServer statusServer = new();
    private readonly string statusPath;
    private StatusSnapshot? lastSnapshot;
    private int statusPortInUse;
    private readonly ProtectionTrigger protection = new();
    private readonly UpdateChecker updates = new();
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60 * 60 * 1000 };
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
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && !settings.SimpleMenu)
            {
                TogglePanel();
            }
            else if (e.Button == MouseButtons.Right)
            {
                OpenPanelPage("tray");
            }
        };
        tray.DoubleClick += (_, _) =>
        {
            if (settings.SimpleMenu)
            {
                OpenPanelPage("history");
            }
        };
        tray.Visible = true;
        if (restoreNotice.Count > 0)
        {
            Notify("IpGuard", string.Join("; ", restoreNotice), Tone.Alert);
        }

        alertSound.Tick += (_, _) => PlayAlertSound();
        quietTimer.Tick += (_, _) =>
        {
            quietTimer.Stop();
            journal.Write("Не беспокоить: время вышло");
        };
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
        alertSound.Dispose();
        toast?.Dispose();
        panel?.Dispose();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        pollTimer.Stop();
        networkTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
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
        if (panel is { IsDisposed: false, Opened: true, AlertActive: true })
        {
            panel.Activate();
            return;
        }

        if (panel is { IsDisposed: false, Opened: true })
        {
            panel.BeginClose();
            return;
        }

        if (panel is { IsDisposed: false } && (DateTime.UtcNow - panel.LastClosedUtc).TotalMilliseconds < 350)
        {
            return;
        }

        var current = EnsurePanel();
        current.SetChecking(checking);
        current.Open(Cursor.Position);
    }

    private PanelServices BuildServices() => new()
    {
        GetSettings = () => settings,
        GetAutostart = Autostart.IsEnabled,
        ApplySettings = (result, autostartResult) => _ = ApplySettingsAsync(result, autostartResult),
        GetState = () => state,
        ReferencesChanged = () =>
        {
            Persist();
            journal.Write("Список эталонов изменён");
            _ = CheckAsync(true, 1, "эталоны");
        },
        GetCurrentIp = () => monitor.Current,
        GetHome = () => settings.HomeCountryCode,
        GetPrograms = ProgramCloser.RunningWithWindows,
        PlaySound = (key, variant, level) => Sounds.Play(key, variant, level),
        GetHistory = () => state.History,
        ClearHistory = () =>
        {
            state.History.Clear();
            Persist();
            journal.Write("История очищена");
        },
        PickSavePath = AskHistoryPath,
        HistoryStamp = () => HashCode.Combine(state.History.Count, state.History.Count > 0 ? state.History[^1].Time : default),
        ReadJournal = () => journal.Tail(2000),
        JournalStamp = journal.Stamp,
        OpenJournalFile = OpenJournal,
        TestAlert = TestAlertNow,
        Version = ProductVersion,
        GetRoutes = RouteLines,
        GetQuietUntil = () => quiet.Until,
        SetQuiet = SetQuiet,
        GetUpdateTag = () => availableRelease?.Tag,
        OpenUpdate = OpenUpdatePage,
        CheckUpdates = () => _ = CheckUpdatesAsync(true),
        Restart = RestartApp,
        Exit = ExitThread,
        ExportBackup = ExportBackup,
        ImportBackup = ImportBackup
    };

    private static string? AskHistoryPath()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт истории",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"ipguard-history-{DateTime.Now:yyyyMMdd}.csv"
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    private async void RunPanelCommand(string id)
    {
        switch (id)
        {
            case "check":
                await CheckAsync(true, 2, "вручную");
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
            ShowNotice("Ошибка", $"Не удалось сохранить файл: {e.Message}");
            return;
        }

        journal.Write($"Настройки сохранены в файл: {dialog.FileName}");
        Notify("IpGuard", "Настройки и эталоны сохранены в файл", Tone.Calm);
    }

    private void ImportBackup()
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
                ShowNotice("Загрузка настроек", "Файл слишком большой для резервной копии настроек.");
                return;
            }

            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice("Ошибка", $"Не удалось прочитать файл: {e.Message}");
            return;
        }

        var result = SettingsBackup.Import(text);
        if (result.Data is not { } data)
        {
            ShowNotice("Загрузка настроек", result.Error ?? "Файл не подошёл.");
            return;
        }

        ShowOnPanel(TextPage.Confirm("Загрузка настроек", SettingsBackup.Describe(result), "Загрузить", () => _ = ApplyImportAsync(data, dialog.FileName)));
    }

    private async Task ApplyImportAsync(BackupData data, string fileName)
    {
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
        journal.Write($"Настройки загружены из файла: {fileName}; эталонов: {data.References.Count}");
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

    private async Task ApplySettingsAsync(Settings result, bool autostartResult)
    {
        settings.CopyFrom(result);
        Sounds.Volume = settings.SoundVolume;
        SaveSettings();
        pollTimer.Interval = Math.Max(15, settings.IntervalSeconds) * 1000;
        if (autostartResult != Autostart.IsEnabled())
        {
            try
            {
                Autostart.Set(autostartResult);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                journal.Write($"Не удалось изменить автозапуск: {e.Message}");
                ShowNotice("Автозапуск", "Не удалось изменить автозапуск: Windows не разрешила запись. Остальные настройки сохранены.");
            }
        }

        ApplyStatusServer();
        journal.Write("Настройки сохранены");
        await CheckAsync(true, 1, "настройки");
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
        var current = EnsurePanel();
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        alertKey = Sounds.KeyFor(verdict.Status);
        alertVariant = alertKey is null ? 1 : settings.SoundVariant(alertKey);
        current.ShowAlert(verdict, verdict.Status == Status.Changed, () => _ = AcceptCurrentAsReferenceAsync(), new Point(work.Right, work.Bottom));
        alertSound.Start();
        PlayAlertSound();
        journal.Write($"Окно тревоги: {verdict.Title}");
    }

    private void TestAlertNow()
    {
        var verdict = new Verdict(Status.Changed, "Проба тревоги", "Так выглядит тревога. На самом деле IP не менялся.", false);
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        alertKey = Sounds.KeyFor(Status.Changed);
        alertVariant = alertKey is null ? 1 : settings.SoundVariant(alertKey);
        EnsurePanel().ShowAlert(verdict, false, null, new Point(work.Right, work.Bottom));
        alertSound.Start();
        PlayAlertSound();
        Notify(verdict.Title, verdict.Message, Tone.Alert);
        journal.Write("Проба тревоги");
    }

    private void PlayAlertSound()
    {
        if (alertKey is not null && settings.SoundEnabled && !quiet.Active)
        {
            Sounds.Play(alertKey, alertVariant);
        }
    }

    private void OpenPanelPage(string id) => ShowOnPanel(null, id);

    private IReadOnlyList<string> RouteLines()
    {
        var info = monitor.Current;
        var abroad = info is not null && info.CountryCode != settings.HomeCountryCode;
        var lines = new List<string>();
        if (abroad && monitor.Proxied)
        {
            lines.Add(monitor.DirectInfo is { } direct ? $"Без прокси: {direct.Ip} · {direct.CountryCode}" : "Без прокси: не определён");
        }

        if (abroad && settings.CheckIpv6)
        {
            lines.Add(monitor.Ipv6Info is { } v6
                ? $"IPv6: {v6.Ip}" + (v6.CountryCode.Length == 2 ? $" · {v6.CountryCode}" : "")
                : "IPv6: нет");
        }

        return lines;
    }

    private void SetQuiet(int minutes)
    {
        if (minutes > 0)
        {
            var span = TimeSpan.FromMinutes(minutes);
            quiet.Start(span);
            quietTimer.Interval = (int)Math.Min(int.MaxValue, span.TotalMilliseconds + 500);
            quietTimer.Stop();
            quietTimer.Start();
            journal.Write($"Не беспокоить: {minutes} мин");
        }
        else
        {
            quiet.Stop();
            quietTimer.Stop();
            journal.Write("Не беспокоить: выключено");
        }
    }

    private void RestartApp()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & start \"\" \"{path}\"") { CreateNoWindow = true, UseShellExecute = false });
        ExitThread();
    }

    private void ShowNotice(string title, string text) => ShowOnPanel(TextPage.Notice(title, text));

    private void ShowOnPanel(Page? page, string? id = null)
    {
        var current = EnsurePanel();
        if (!current.Visible || !current.Opened)
        {
            current.SetChecking(checking);
            current.Open(Cursor.Position);
        }

        if (page is not null)
        {
            current.ShowPage(page);
        }
        else if (id is not null)
        {
            current.ShowPage(id);
        }
    }

    private NebulaPanel EnsurePanel()
    {
        if (panel is null or { IsDisposed: true })
        {
            panel = new NebulaPanel(RunPanelCommand, services: BuildServices());
            panel.AlertDismissed += alertSound.Stop;
            panel.Apply(panelState);
        }

        return panel;
    }

    private void Notify(string title, string message, Tone tone)
    {
        if (ToastPolicy.SystemShouldDecideNow())
        {
            tray.ShowBalloonTip(6000, title, message, tone == Tone.Calm ? ToolTipIcon.Info : ToolTipIcon.Warning);
            return;
        }

        try
        {
            if (toast is null or { IsDisposed: true })
            {
                toast = new ToastWindow();
                toast.Clicked += () =>
                {
                    if (panel is not { IsDisposed: false, Opened: true })
                    {
                        TogglePanel();
                    }
                };
            }

            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            toast.Present(title, message, tone, screen.Bounds, screen.WorkingArea);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            journal.Write(ErrorText.Format("уведомление", e));
            tray.ShowBalloonTip(6000, title, message, tone == Tone.Calm ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
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
        var summary = CheckRecord.Summary(verdict, info, SystemProxy.Current() is not null, primary.PausedSources);
        if (reason == "таймер" && !verdict.Notify && summary == lastRecord)
        {
            return;
        }

        lastRecord = summary;
        journal.Write(CheckRecord.Line(reason, summary, verdict, info, monitor.Leak, watcher.LastOutage, monitor.Disagreement));
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
            if (outcome.Available is { } release)
            {
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
            Notify("IpGuard", outcome.Message, outcome.Failed ? Tone.Alert : Tone.Calm);
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

        var current = EnsurePanel();
        if (current.ProtectActive)
        {
            return;
        }

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        current.ShowProtect(
            text,
            programs,
            settings.ProtectGraceSeconds,
            () => CloseProtectedPrograms(programs),
            () => journal.Write("Защита: закрытие отменено"),
            new Point(work.Right, work.Bottom));
    }

    private void CloseProtectedPrograms(IReadOnlyList<string> programs)
    {
        var report = ProgramCloser.Close(programs);
        journal.Write($"Защита: {report.Summary}");
        Notify("IpGuard", $"Защита: {report.Summary}", Tone.Alert);
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

        tray.Icon = TrayIcons.For(verdict.Status);
        var tip = info is null ? "IpGuard: нет связи" : $"{info.CountryCode} {info.Ip} — {Labels.For(verdict.Status)}";
        tray.Text = tip.Length > 63 ? tip[..63] : tip;
        var abroad = info is not null && info.CountryCode != settings.HomeCountryCode;
        var note = "";
        if (abroad)
        {
            var references = state.ReferencesFor(info!.CountryCode);
            var text = references.Count == 0
                ? $"Эталон для {info.CountryCode} не задан"
                : $"{(references.Count == 1 ? "Эталон" : "Эталоны")} для {info.CountryCode}: {IpWatcher.DescribeReferences(references)}";
            note = text.Length > 70 ? text[..69] + "…" : text;
        }

        panelState = PanelState.From(verdict, info, note);
        panel?.Apply(panelState);

        var alarm = verdict.Status is Status.Changed or Status.Leak;
        var plan = NotificationPolicy.Decide(verdict, settings.AlertWindow, settings.SoundEnabled, quiet.Active);
        if (plan.Window)
        {
            ShowAlert(verdict);
        }
        else if (!(alarm && settings.AlertWindow) && panel is { IsDisposed: false, AlertActive: true })
        {
            alertSound.Stop();
            panel.ResolveAlert($"{verdict.Title}. {verdict.Message}");
        }

        if (plan.Balloon)
        {
            Notify(verdict.Title, verdict.Message, PanelState.ToneOf(verdict.Status));
        }

        if (plan.Sound && Sounds.KeyFor(verdict.Status) is { } key)
        {
            Sounds.Play(key, settings.SoundVariant(key));
        }

        EvaluateProtection(verdict);
    }
}
