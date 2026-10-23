namespace IpGuard;

public sealed class SettingsPage : Page
{
    private const string ProtectHint = "Программам из списка придёт обычная просьба закрыть окно. Если в них есть несохранённая работа, они сами спросят про сохранение. По умолчанию выключено.";
    private const string ProtectConfirm = "Когда сработает опасная ситуация, программам из списка придёт просьба закрыть окно. Если в программе есть несохранённая работа и она не спросит про сохранение, данные могут быть потеряны. Включить защиту?";

    private readonly PanelServices services;
    private readonly SettingsDraft draft;
    private bool suppress;

    public SettingsPage(PanelServices services)
        : base("Настройки")
    {
        this.services = services;
        draft = new SettingsDraft(services.GetSettings(), services.GetAutostart());
        Footer = new[]
        {
            new FooterButton("Отмена", false, () => Host?.Pop()),
            new FooterButton("Сохранить", true, Save)
        };
        Build();
    }

    public SettingsDraft Draft => draft;

    public override void OnShown() => Build();

    private void Save()
    {
        services.ApplySettings(draft.Result(), draft.Autostart);
        Host?.Pop();
    }

    private void Build()
    {
        var focusId = Rows.Focus >= 0 && Rows.Focus < Rows.Rows.Count ? Rows.Rows[Rows.Focus].Id : "";
        var scroll = Rows.ScrollY;
        var s = draft.Working;
        var rows = new List<Row>
        {
            new SectionRow("Основное"),
            CountryRow(),
            Interval(),
            Toggle("autostart", "Запускать вместе с Windows", draft.Autostart, v => draft.Autostart = v),
            Toggle("simple", "Простое меню", s.SimpleMenu, v => s.SimpleMenu = v, "левый клик по значку без окна"),
            new SectionRow("Проверки"),
            Toggle("direct", "Прямой маршрут (без прокси)", s.CheckDirectRoute, v => s.CheckDirectRoute = v),
            Toggle("ipv6", "Проверять IPv6", s.CheckIpv6, v => s.CheckIpv6 = v),
            Outage(),
            new SectionRow("Уведомления"),
            Toggle("alert", "Окно тревоги поверх всех окон", s.AlertWindow, v => s.AlertWindow = v),
            Toggle("sound", "Звуки", s.SoundEnabled, v => s.SoundEnabled = v)
        };
        if (services.GetCurrentIp() is { CountryCode.Length: > 0 } current && !string.Equals(current.CountryCode, s.HomeCountryCode, StringComparison.OrdinalIgnoreCase))
        {
            rows.Insert(2, new ButtonRow($"Считать домашней: {Countries.Describe(current.CountryCode)}") { Id = "home-current", Action = () =>
            {
                draft.Working.HomeCountryCode = current.CountryCode;
                Build();
            } });
        }

        var volume = new SliderRow("Громкость", 0, 100, 5, s.SoundVolume) { Id = "volume" };
        volume.Changed = () => s.SoundVolume = volume.Value;
        rows.Add(volume);
        foreach (var (key, title, names) in Sounds.Kinds)
        {
            var row = new ChoiceRow(title, names, s.SoundVariant(key) - 1) { Id = "sound-" + key, ShowPlay = true };
            row.Changed = () => s.Sounds[key] = row.Index + 1;
            row.PlayAction = () => services.PlaySound(key, row.Index + 1, volume.Value);
            rows.Add(row);
        }

        rows.Add(new ButtonRow("Проверить тревогу") { Id = "test-alert", Action = services.TestAlert });
        rows.Add(new SectionRow("Для скриптов"));
        var status = Toggle("status", "Статус по локальному адресу", s.StatusServerEnabled, v => s.StatusServerEnabled = v, StatusHint(s.StatusPort));
        rows.Add(status);
        var port = Stepper("port", "Порт", SettingsRules.MinPort, SettingsRules.MaxPort, 1, SettingsRules.ClampPort(s.StatusPort), v => s.StatusPort = v, "Порт", "Порт для локального адреса статуса, от 1024 до 65535:");
        port.Changed += () => status.Hint = StatusHint(port.Value);
        rows.Add(port);
        rows.Add(new SectionRow("Обновления"));
        rows.Add(Toggle("updates", "Проверять новую версию", s.UpdateCheckEnabled, v => s.UpdateCheckEnabled = v, "раз в сутки, запрос к GitHub"));
        rows.Add(new SectionRow("Резервная копия"));
        rows.Add(new ButtonRow("Сохранить настройки в файл...") { Id = "backup-export", Action = services.ExportBackup });
        rows.Add(new ButtonRow("Загрузить настройки из файла...") { Id = "backup-import", Action = services.ImportBackup });
        rows.Add(new SectionRow("Защита"));
        rows.Add(ProtectToggle());
        rows.Add(new NoteRow(ProtectHint));
        if (!draft.Working.ProtectEnabled)
        {
            rows.Add(new NoteRow("Пока защита выключена, пункты ниже недоступны: включите её переключателем выше.") { Id = "protect-lock" });
        }

        rows.Add(Toggle("protect-alarm", "При красной тревоге", s.ProtectOnAlarm, v => s.ProtectOnAlarm = v));
        var outage = Toggle("protect-outage", "При потере связи дольше", s.ProtectOnOutage, v => s.ProtectOnOutage = v);
        outage.Changed += UpdateEnabled;
        rows.Add(outage);
        rows.Add(Stepper("protect-outage-seconds", "секунд без связи", 5, 3600, 5, Math.Clamp(s.ProtectOutageSeconds, 5, 3600), v => s.ProtectOutageSeconds = v, "Потеря связи", "Сколько секунд без связи считать опасной ситуацией, от 5 до 3600:"));
        rows.Add(Stepper("protect-grace", "Время на отмену, с", 0, 120, 5, Math.Clamp(s.ProtectGraceSeconds, 0, 120), v => s.ProtectGraceSeconds = v, "Время на отмену", "Сколько секунд даётся на отмену закрытия, от 0 до 120:"));
        rows.Add(new SectionRow("Какие программы закрывать"));
        for (var i = 0; i < draft.Programs.Count; i++)
        {
            var index = i;
            rows.Add(new ItemRow(draft.Programs[i], "") { Id = "program-" + draft.Programs[i], ShowRemove = true, RemoveAction = () =>
            {
                draft.RemoveProgram(index);
                Build();
            } });
        }

        rows.Add(new ButtonRow("Добавить запущенную...") { Id = "add-running", Action = AddRunning });
        rows.Add(new ButtonRow("Добавить по имени...") { Id = "add-name", Action = AddByName });
        Rows.Replace(rows);
        UpdateEnabled();
        if (focusId.Length > 0)
        {
            var index = Rows.Rows.ToList().FindIndex(r => r.Id == focusId);
            Rows.SetFocus(index);
        }

        Rows.ScrollBy(scroll);
    }

    private PickerRow CountryRow()
    {
        var row = new PickerRow("Домашняя страна", Countries.Describe(draft.Working.HomeCountryCode)) { Id = "country" };
        row.Open = () => Host?.Push(new CountryPickerPage(draft.Working.HomeCountryCode, code =>
        {
            draft.Working.HomeCountryCode = code;
            row.SetValue(Countries.Describe(code));
        }));
        return row;
    }

    private StepperRow Interval() =>
        Stepper("interval", "Интервал проверки, с", SettingsRules.MinInterval, SettingsRules.MaxInterval, 15, SettingsRules.ClampInterval(draft.Working.IntervalSeconds), v => draft.Working.IntervalSeconds = v, "Интервал проверки", "Секунд между проверками, от 15 до 3600:");

    private ChoiceRow Outage()
    {
        var titles = SettingsDraft.OutageChoices.Select(c => c.Title).ToArray();
        var index = Array.FindIndex(SettingsDraft.OutageChoices, c => c.Seconds == draft.Working.OutageSeconds);
        var row = new ChoiceRow("Сообщать о потере связи", titles, index >= 0 ? index : 2) { Id = "outage" };
        row.Changed = () => draft.Working.OutageSeconds = SettingsDraft.OutageChoices[row.Index].Seconds;
        return row;
    }

    private static ToggleRow Toggle(string id, string label, bool value, Action<bool> set, string? hint = null)
    {
        var row = new ToggleRow(label, value) { Id = id, Hint = hint };
        row.Changed = () => set(row.Value);
        return row;
    }

    private StepperRow Stepper(string id, string label, int min, int max, int step, int value, Action<int> set, string promptTitle, string promptCaption)
    {
        var row = new StepperRow(label, min, max, step, value) { Id = id };
        row.Changed = () => set(row.Value);
        row.Prompt = () => Host?.Push(new PromptPage(
            promptTitle,
            promptCaption,
            row.Value.ToString(),
            max.ToString().Length,
            text => int.TryParse(text, out var n) && n >= min && n <= max ? null : $"Введите число от {min} до {max}",
            text => row.SetValue(int.Parse(text)),
            digitsOnly: true));
        return row;
    }

    private ToggleRow ProtectToggle()
    {
        var row = new ToggleRow("Закрывать программы при опасной ситуации", draft.Working.ProtectEnabled) { Id = "protect" };
        row.Changed = () =>
        {
            if (suppress)
            {
                return;
            }

            if (row.Value)
            {
                suppress = true;
                row.Set(false);
                suppress = false;
                Host?.Push(new ConfirmPage("Защита", ProtectConfirm, "Включить", () =>
                {
                    draft.Working.ProtectEnabled = true;
                    Build();
                }));
                return;
            }

            draft.Working.ProtectEnabled = false;
            Build();
        };
        return row;
    }

    private void UpdateEnabled()
    {
        var on = draft.Working.ProtectEnabled;
        var outage = Rows.Rows.OfType<ToggleRow>().FirstOrDefault(r => r.Id == "protect-outage");
        foreach (var row in Rows.Rows)
        {
            switch (row.Id)
            {
                case "protect-alarm" or "protect-outage" or "protect-grace" or "add-running" or "add-name":
                    row.Enabled = on;
                    break;
                case "protect-outage-seconds":
                    row.Enabled = on && outage is { Value: true };
                    break;
                default:
                    if (row.Id.StartsWith("program-", StringComparison.Ordinal))
                    {
                        row.Enabled = on;
                    }

                    break;
            }
        }
    }

    private void AddRunning() => Host?.Push(new ProgramPickerPage(services.GetPrograms(), name => AddError(draft.AddProgram(name), name)));

    private void AddByName() => Host?.Push(new PromptPage(
        "Программа",
        "Имя процесса без .exe, например WindowsTerminal или chrome:",
        "",
        40,
        text => AddError(ProbeAdd(text), text),
        text => draft.AddProgram(text)));

    private ProgramAdd ProbeAdd(string text)
    {
        var clean = ProgramCloser.Normalize(text);
        if (clean.Length == 0)
        {
            return ProgramAdd.Empty;
        }

        if (ProgramCloser.IsProtected(clean))
        {
            return ProgramAdd.Protected;
        }

        return draft.Programs.Any(item => string.Equals(item, clean, StringComparison.OrdinalIgnoreCase)) ? ProgramAdd.Duplicate : ProgramAdd.Added;
    }

    private static string? AddError(ProgramAdd result, string name) => result switch
    {
        ProgramAdd.Empty => "Введите имя программы",
        ProgramAdd.Protected => $"Программу «{ProgramCloser.Normalize(name)}» добавить нельзя: она нужна системе или самой IpGuard.",
        ProgramAdd.Duplicate => "Эта программа уже в списке",
        _ => null
    };

    private static string StatusHint(int port) => $"http://127.0.0.1:{SettingsRules.ClampPort(port)}/status, только этот компьютер";
}
