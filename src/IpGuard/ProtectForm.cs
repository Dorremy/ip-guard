namespace IpGuard;

public sealed class ProtectForm : Form
{
    private readonly Label title = new();
    private readonly Label message = new();
    private readonly Button closeNow = new() { Text = "Закрыть сейчас", AutoSize = true };
    private readonly Button keep = new() { Text = "Не закрывать", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly string reason;
    private readonly IReadOnlyList<string> programs;
    private readonly Action onClose;
    private readonly Action onCancel;
    private bool finished;

    public ProtectForm(string reason, IReadOnlyList<string> programs, int seconds, Action onClose, Action onCancel)
    {
        this.reason = reason;
        this.programs = programs;
        this.onClose = onClose;
        this.onCancel = onCancel;
        Remaining = Math.Max(0, seconds);

        Text = "IpGuard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(560, 240);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + 140);

        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(200, 110, 20) };
        title.Dock = DockStyle.Fill;
        title.ForeColor = Color.White;
        title.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.Padding = new Padding(16, 0, 16, 0);
        title.Text = "IpGuard закроет программы";
        header.Controls.Add(title);

        message.Dock = DockStyle.Fill;
        message.Font = new Font("Segoe UI", 10.5f);
        message.Padding = new Padding(16, 12, 16, 8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 10)
        };
        buttons.Controls.Add(keep);
        buttons.Controls.Add(closeNow);

        closeNow.Click += (_, _) => Finish(true);
        keep.Click += (_, _) => Finish(false);
        timer.Tick += (_, _) => Advance();
        FormClosed += (_, _) =>
        {
            timer.Stop();
            if (!finished)
            {
                finished = true;
                onCancel();
            }
        };

        Controls.Add(message);
        Controls.Add(buttons);
        Controls.Add(header);
        AcceptButton = keep;
        CancelButton = keep;
        Render();
    }

    public int Remaining { get; private set; }

    public bool Done => finished;

    public string MessageText => message.Text;

    protected override bool ShowWithoutActivation => true;

    public void Begin()
    {
        if (!Visible)
        {
            Show();
        }

        BringToFront();
        timer.Start();
    }

    public void Advance()
    {
        if (finished)
        {
            return;
        }

        Remaining--;
        if (Remaining <= 0)
        {
            Finish(true);
            return;
        }

        Render();
    }

    private void Finish(bool close)
    {
        if (finished)
        {
            return;
        }

        finished = true;
        timer.Stop();
        if (close)
        {
            onClose();
        }
        else
        {
            onCancel();
        }

        Close();
    }

    private void Render()
    {
        message.Text = $"{reason}{Environment.NewLine}{Environment.NewLine}"
            + $"Через {Remaining} с программам из списка придёт просьба закрыть окно: {string.Join(", ", programs)}.{Environment.NewLine}"
            + "Если в них есть несохранённая работа, они сами спросят про сохранение. Принудительно ничего не завершается.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
