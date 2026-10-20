namespace IpGuard;

public sealed class SettingsForm : Form
{
    public const int MinInterval = 15;
    public const int MaxInterval = 3600;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    private static readonly (int Seconds, string Title)[] OutageChoices =
    {
        (0, "Сразу"), (15, "Через 15 секунд"), (30, "Через 30 секунд"), (60, "Через минуту")
    };

    private readonly Func<bool> confirmProtect;
    private readonly Func<string?> pickRunning;
    private readonly Func<string?> askName;
    private readonly Action<string> warn;
    private bool loading;
    private readonly CheckBox protectBox = new() { Text = "Закрывать выбранные программы при опасной ситуации", AutoSize = true };
    private readonly Label protectHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(480, 0), Text = "Программам из списка придёт обычная просьба закрыть окно. Если в них есть несохранённая работа, они сами спросят про сохранение. По умолчанию выключено." };
    private readonly CheckBox protectAlarm = new() { Text = "При красной тревоге (IP отличается, утечка, прокси не отвечает)", AutoSize = true };
    private readonly CheckBox protectOutage = new() { Text = "При потере связи дольше, секунд", AutoSize = true };
    private readonly NumericUpDown protectOutageSeconds = new() { Minimum = 5, Maximum = 3600, Width = 70 };
    private readonly NumericUpDown protectGrace = new() { Minimum = 0, Maximum = 120, Width = 70 };
    private readonly ListBox protectList = new() { Width = 480, Height = 78 };
    private readonly Button protectAddRunning = new() { Text = "Добавить запущенную...", AutoSize = true, Name = "protect-add-running" };
    private readonly Button protectAddName = new() { Text = "Добавить по имени...", AutoSize = true, Name = "protect-add-name" };
    private readonly Button protectRemove = new() { Text = "Убрать", AutoSize = true, Name = "protect-remove" };
    private readonly Settings working;
    private readonly bool autostart;
    private readonly Action<string, int, int> preview;
    private readonly ComboBox country = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown interval = new() { Minimum = MinInterval, Maximum = MaxInterval, Width = 80 };
    private readonly CheckBox autostartBox = new() { Text = "Запускать вместе с Windows", AutoSize = true };
    private readonly CheckBox direct = new() { Text = "Проверять прямой маршрут (без прокси)", AutoSize = true };
    private readonly CheckBox ipv6 = new() { Text = "Проверять IPv6", AutoSize = true };
    private readonly ComboBox outage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly CheckBox alertWindow = new() { Text = "Показывать окно тревоги поверх всех окон", AutoSize = true };
    private readonly CheckBox soundBox = new() { Text = "Звуки включены", AutoSize = true };
    private readonly CheckBox statusBox = new() { Text = "Отдавать статус скриптам на этом компьютере", AutoSize = true };
    private readonly NumericUpDown statusPort = new() { Minimum = MinPort, Maximum = MaxPort, Width = 80 };
    private readonly Label statusHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly CheckBox updatesBox = new() { Text = "Проверять наличие новой версии раз в сутки (запрос к GitHub)", AutoSize = true };
    private readonly TrackBar volume = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 260, Height = 36 };
    private readonly Dictionary<string, ComboBox> variants = new();
    private readonly Button save = new() { Text = "Сохранить", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button cancel = new() { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };

    public SettingsForm(
        Settings current,
        bool autostartEnabled,
        Action<string, int, int>? preview = null,
        Func<bool>? confirmProtect = null,
        Func<string?>? pickRunning = null,
        Func<string?>? askName = null,
        Action<string>? warn = null)
    {
        this.confirmProtect = confirmProtect ?? ConfirmProtectDialog;
        this.pickRunning = pickRunning ?? (() => ProgramPickerForm.Pick());
        this.askName = askName ?? (() => PromptForm.Ask("Программа", "Имя процесса без .exe, например WindowsTerminal или chrome:", ""));
        this.warn = warn ?? (text => MessageBox.Show(text, "IpGuard", MessageBoxButtons.OK, MessageBoxIcon.Information));
        working = Clone(current);
        autostart = autostartEnabled;
        this.preview = preview ?? ((key, variant, level) => Sounds.Play(key, variant, level));

        Text = "Настройки IpGuard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        ClientSize = new Size(580, Math.Min(860, area.Height - 100));
        AcceptButton = save;
        CancelButton = cancel;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12)
        };

        root.Controls.Add(Group("Основное", Row("Домашняя страна", country), Row("Интервал проверки, секунд", interval), autostartBox));
        root.Controls.Add(Group("Проверки", direct, ipv6, Row("Сообщать о потере связи", outage)));
        root.Controls.Add(Group("Уведомления", alertWindow, soundBox, Row("Громкость", volume), VariantRows()));
        root.Controls.Add(Group("Для скриптов", statusBox, Row("Порт", statusPort), statusHint));
        root.Controls.Add(Group("Обновления", updatesBox));
        root.Controls.Add(Group("Защита", protectBox, protectHint, protectAlarm, OutageRow(), Row("Время на отмену, секунд", protectGrace), new Label { Text = "Какие программы закрывать:", AutoSize = true }, protectList, ProtectButtons()));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 10)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        Controls.Add(root);
        Controls.Add(buttons);

        Fill();
        statusPort.ValueChanged += (_, _) => UpdateStatusHint();
        protectBox.CheckedChanged += (_, _) => OnProtectToggled();
        protectOutage.CheckedChanged += (_, _) => UpdateProtectEnabled();
        protectAddRunning.Click += (_, _) => AddProgram(this.pickRunning());
        protectAddName.Click += (_, _) => AddProgram(this.askName());
        protectRemove.Click += (_, _) => RemoveSelectedProgram();
        protectList.SelectedIndexChanged += (_, _) => UpdateProtectEnabled();
        save.Click += (_, _) => Collect();
    }

    public Settings Result => working;

    public bool AutostartResult => autostartBox.Checked;

    public static Settings Clone(Settings source)
    {
        var copy = new Settings();
        copy.CopyFrom(source);
        return copy;
    }

    public IReadOnlyList<string> Programs => protectList.Items.Cast<string>().ToList();

    public bool ProtectControlsEnabled => protectAlarm.Enabled;

    public enum AddResult
    {
        Added,
        Empty,
        Protected,
        Duplicate
    }

    public AddResult AddProgram(string? name)
    {
        var clean = ProgramCloser.Normalize(name);
        if (clean.Length == 0)
        {
            return AddResult.Empty;
        }

        if (ProgramCloser.IsProtected(clean))
        {
            warn($"Программу «{clean}» добавить нельзя: она нужна системе или самой IpGuard.");
            return AddResult.Protected;
        }

        if (protectList.Items.Cast<string>().Any(item => string.Equals(item, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return AddResult.Duplicate;
        }

        protectList.Items.Add(clean);
        return AddResult.Added;
    }

    public bool RemoveProgram(int index)
    {
        if (index < 0 || index >= protectList.Items.Count)
        {
            return false;
        }

        protectList.Items.RemoveAt(index);
        UpdateProtectEnabled();
        return true;
    }

    private void RemoveSelectedProgram() => RemoveProgram(protectList.SelectedIndex);

    private void OnProtectToggled()
    {
        if (!loading && protectBox.Checked && !confirmProtect())
        {
            protectBox.Checked = false;
            return;
        }

        UpdateProtectEnabled();
    }

    private void UpdateProtectEnabled()
    {
        var on = protectBox.Checked;
        protectAlarm.Enabled = on;
        protectOutage.Enabled = on;
        protectOutageSeconds.Enabled = on && protectOutage.Checked;
        protectGrace.Enabled = on;
        protectList.Enabled = on;
        protectAddRunning.Enabled = on;
        protectAddName.Enabled = on;
        protectRemove.Enabled = on && protectList.SelectedIndex >= 0;
    }

    private Control OutageRow()
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(protectOutage);
        row.Controls.Add(protectOutageSeconds);
        return row;
    }

    private Control ProtectButtons()
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(protectAddRunning);
        row.Controls.Add(protectAddName);
        row.Controls.Add(protectRemove);
        return row;
    }

    private static bool ConfirmProtectDialog() =>
        MessageBox.Show(
            "Когда сработает опасная ситуация, программам из списка придёт просьба закрыть окно. Если в программе есть несохранённая работа и она не спросит про сохранение, данные могут быть потеряны." + Environment.NewLine + Environment.NewLine + "Включить защиту?",
            "IpGuard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    public static int ClampInterval(int seconds) => Math.Clamp(seconds, MinInterval, MaxInterval);

    public static int ClampPort(int port) => port is >= MinPort and <= MaxPort ? port : new Settings().StatusPort;

    private void UpdateStatusHint()
    {
        statusHint.Text = $"Адрес: http://127.0.0.1:{(int)statusPort.Value}/status (только этот компьютер)";
    }

    private void Fill()
    {
        foreach (var (code, title) in Countries.Choices(working.HomeCountryCode))
        {
            country.Items.Add(new Choice(code, title));
        }

        country.SelectedIndex = Math.Max(0, country.Items.Cast<Choice>().ToList().FindIndex(c => c.Code == working.HomeCountryCode));
        interval.Value = ClampInterval(working.IntervalSeconds);
        autostartBox.Checked = autostart;
        direct.Checked = working.CheckDirectRoute;
        ipv6.Checked = working.CheckIpv6;
        alertWindow.Checked = working.AlertWindow;
        soundBox.Checked = working.SoundEnabled;
        volume.Value = Math.Clamp(working.SoundVolume, 0, 100);
        loading = true;
        updatesBox.Checked = working.UpdateCheckEnabled;
        protectBox.Checked = working.ProtectEnabled;
        protectAlarm.Checked = working.ProtectOnAlarm;
        protectOutage.Checked = working.ProtectOnOutage;
        protectOutageSeconds.Value = Math.Clamp(working.ProtectOutageSeconds, 5, 3600);
        protectGrace.Value = Math.Clamp(working.ProtectGraceSeconds, 0, 120);
        protectList.Items.Clear();
        foreach (var program in working.ProtectPrograms)
        {
            protectList.Items.Add(program);
        }

        loading = false;
        UpdateProtectEnabled();
        statusBox.Checked = working.StatusServerEnabled;
        statusPort.Value = ClampPort(working.StatusPort);
        UpdateStatusHint();

        foreach (var (seconds, title) in OutageChoices)
        {
            outage.Items.Add(new Choice(seconds.ToString(), title));
        }

        var index = Array.FindIndex(OutageChoices, item => item.Seconds == working.OutageSeconds);
        outage.SelectedIndex = index >= 0 ? index : 2;
    }

    private void Collect()
    {
        working.HomeCountryCode = ((Choice)country.SelectedItem!).Code;
        working.IntervalSeconds = ClampInterval((int)interval.Value);
        working.CheckDirectRoute = direct.Checked;
        working.CheckIpv6 = ipv6.Checked;
        working.OutageSeconds = OutageChoices[outage.SelectedIndex].Seconds;
        working.AlertWindow = alertWindow.Checked;
        working.SoundEnabled = soundBox.Checked;
        working.SoundVolume = volume.Value;
        working.UpdateCheckEnabled = updatesBox.Checked;
        working.ProtectEnabled = protectBox.Checked;
        working.ProtectOnAlarm = protectAlarm.Checked;
        working.ProtectOnOutage = protectOutage.Checked;
        working.ProtectOutageSeconds = (int)protectOutageSeconds.Value;
        working.ProtectGraceSeconds = (int)protectGrace.Value;
        working.ProtectPrograms = protectList.Items.Cast<string>().ToList();
        working.StatusServerEnabled = statusBox.Checked;
        working.StatusPort = ClampPort((int)statusPort.Value);
        foreach (var (key, box) in variants)
        {
            working.Sounds[key] = box.SelectedIndex + 1;
        }
    }

    private Control VariantRows()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        foreach (var (key, title, names) in Sounds.Kinds)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            box.Items.AddRange(names);
            box.SelectedIndex = working.SoundVariant(key) - 1;
            variants[key] = box;

            var play = new Button { Text = "▶", Width = 36, Height = 26, Name = "play-" + key };
            play.Click += (_, _) => preview(key, box.SelectedIndex + 1, volume.Value);

            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            row.Controls.Add(new Label { Text = title, Width = 190, TextAlign = ContentAlignment.MiddleLeft, Height = 26 });
            row.Controls.Add(box);
            row.Controls.Add(play);
            panel.Controls.Add(row);
        }

        return panel;
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var inner = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(8, 4, 8, 8)
        };
        inner.Controls.AddRange(controls);

        var box = new GroupBox { Text = title, AutoSize = true, MinimumSize = new Size(520, 0), MaximumSize = new Size(520, 0), Padding = new Padding(6) };
        box.Controls.Add(inner);
        return box;
    }

    private static Control Row(string label, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(new Label { Text = label, Width = 190, TextAlign = ContentAlignment.MiddleLeft, Height = 26 });
        row.Controls.Add(control);
        return row;
    }

    private sealed record Choice(string Code, string Title)
    {
        public override string ToString() => Title;
    }
}
