namespace IpGuard;

public sealed class AlertForm : Form
{
    private static readonly Color Alarm = Color.FromArgb(200, 50, 40);
    private static readonly Color Calm = Color.FromArgb(40, 150, 90);

    private readonly Panel header = new() { Dock = DockStyle.Top, Height = 64 };
    private readonly Label title = new();
    private readonly Label message = new();
    private readonly Button accept = new() { Text = "Принять текущий IP как эталон", AutoSize = true };
    private readonly Button dismiss = new() { Text = "Понятно", AutoSize = true };
    private readonly System.Windows.Forms.Timer repeat = new() { Interval = 4000 };
    private readonly Func<bool> soundEnabled;
    private string? soundKey;
    private int soundVariant;
    private Action? onAccept;

    public AlertForm(Func<bool> soundEnabled)
    {
        this.soundEnabled = soundEnabled;

        Text = "IpGuard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(560, 250);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + 80);

        title.Dock = DockStyle.Fill;
        title.ForeColor = Color.White;
        title.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.Padding = new Padding(16, 0, 16, 0);
        header.Controls.Add(title);

        message.Dock = DockStyle.Fill;
        message.Font = new Font("Segoe UI", 11f);
        message.Padding = new Padding(16, 14, 16, 8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 10)
        };
        buttons.Controls.Add(dismiss);
        buttons.Controls.Add(accept);

        dismiss.Click += (_, _) => Close();
        accept.Click += (_, _) =>
        {
            onAccept?.Invoke();
            Close();
        };
        repeat.Tick += (_, _) => PlaySound();
        FormClosed += (_, _) => repeat.Stop();

        Controls.Add(message);
        Controls.Add(buttons);
        Controls.Add(header);
        AcceptButton = dismiss;
    }

    public bool Active => Visible && !IsDisposed && header.BackColor == Alarm;

    public string TitleText => title.Text;

    public string MessageText => message.Text;

    public bool AcceptVisible => accept.Visible;

    public string DismissText => dismiss.Text;

    protected override bool ShowWithoutActivation => true;

    public void Raise(Verdict verdict, string? key, int variant, bool canAccept, Action? accept)
    {
        header.BackColor = Alarm;
        title.Text = verdict.Title;
        message.Text = verdict.Message.Replace(". ", ".\n").Replace("\n", Environment.NewLine);
        this.accept.Visible = canAccept;
        dismiss.Text = "Понятно";
        onAccept = accept;
        soundKey = key;
        soundVariant = variant;

        if (!Visible)
        {
            Show();
        }

        TopMost = true;
        BringToFront();
        repeat.Start();
        PlaySound();
    }

    public void Resolve(string text)
    {
        repeat.Stop();
        header.BackColor = Calm;
        title.Text = "Снова всё в порядке";
        message.Text = text;
        accept.Visible = false;
        dismiss.Text = "Закрыть";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            repeat.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PlaySound()
    {
        if (soundKey is not null && soundEnabled())
        {
            Sounds.Play(soundKey, soundVariant);
        }
    }
}
