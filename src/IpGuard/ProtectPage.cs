namespace IpGuard;

public sealed class ProtectPage : Page
{
    private readonly int seconds;
    private readonly Action onClose;
    private readonly Action onCancel;
    private readonly Action onLeft;
    private readonly NoteRow countdown = new("") { Id = "countdown" };
    private double? started;
    private double lastNow;
    private bool left;

    public ProtectPage(string reason, IReadOnlyList<string> programs, int seconds, Action onClose, Action onCancel, Action onLeft)
        : base("Закрыть программы?")
    {
        this.seconds = Math.Max(0, seconds);
        this.onClose = onClose;
        this.onCancel = onCancel;
        this.onLeft = onLeft;
        Remaining = this.seconds;
        Rows.Replace(new Row[]
        {
            new LineRow(reason.EndsWith('.') ? reason : reason + ".", LineLevel.Alert),
            countdown,
            new NoteRow($"Программы: {string.Join(", ", programs)}."),
            new NoteRow("Если в них есть несохранённая работа, они сами спросят про сохранение. Принудительно ничего не завершается.")
        });
        Footer = new[]
        {
            new FooterButton("Не закрывать", false, () => Host?.Pop()),
            new FooterButton("Закрыть сейчас", true, () => Complete(true))
        };
        Show();
    }

    public int Remaining { get; private set; }

    public bool Finished { get; private set; }

    public override void OnTick(double now)
    {
        if (Finished)
        {
            return;
        }

        if (started is null || now < lastNow)
        {
            started = now;
        }

        lastNow = now;
        var timeLeft = seconds - (now - started.Value);
        Remaining = Math.Max(0, (int)Math.Ceiling(timeLeft - 1e-9));
        if (seconds == 0 || timeLeft <= 0)
        {
            Remaining = 0;
            Complete(true);
            return;
        }

        Show();
        Rows.Refresh();
    }

    public override bool OnBack()
    {
        if (!Finished)
        {
            Finished = true;
            onCancel();
        }

        Leave();
        return true;
    }

    public void Abort()
    {
        if (Finished)
        {
            return;
        }

        Finished = true;
        onCancel();
        Leave();
    }

    private void Complete(bool close)
    {
        if (Finished)
        {
            return;
        }

        Finished = true;
        if (close)
        {
            onClose();
        }
        else
        {
            onCancel();
        }

        Host?.Pop();
    }

    private void Leave()
    {
        if (left)
        {
            return;
        }

        left = true;
        onLeft();
    }

    private void Show() =>
        countdown.SetText($"Через {Remaining} с программам из списка придёт просьба закрыть окно.");
}
