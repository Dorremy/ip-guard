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
    private readonly TrackBar volume = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 260, Height = 36 };
    private readonly Dictionary<string, ComboBox> variants = new();
    private readonly Button save = new() { Text = "Сохранить", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button cancel = new() { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };

    public SettingsForm(Settings current, bool autostartEnabled, Action<string, int, int>? preview = null)
    {
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
