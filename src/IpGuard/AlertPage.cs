namespace IpGuard;

public sealed class AlertPage : Page
{
    public const int TitleMax = 26;

    private readonly Action onDismiss;

    public AlertPage(string title, string message, bool canAccept, Action onAccept, Action onDismiss)
        : base(Short(title))
    {
        this.onDismiss = onDismiss;
        Show(title, message, canAccept, onAccept);
    }

    public bool Resolved { get; private set; }

    public void Show(string title, string message, bool canAccept, Action onAccept)
    {
        Resolved = false;
        Title = Short(title);
        Rows.Replace(Lines(message, LineLevel.Alert));
        var buttons = new List<FooterButton> { new("Понятно", false, Leave) };
        if (canAccept)
        {
            buttons.Add(new FooterButton("Принять IP", true, () =>
            {
                onAccept();
                Leave();
            }));
        }

        Footer = buttons;
    }

    public void Resolve(string text)
    {
        Resolved = true;
        Title = "Снова всё в порядке";
        Rows.Replace(Lines(text, LineLevel.Normal));
        Footer = new[] { new FooterButton("Закрыть", true, Leave) };
    }

    public override bool OnBack()
    {
        onDismiss();
        return true;
    }

    private void Leave() => Host?.Pop();

    private static string Short(string title)
    {
        var t = (title ?? "").Trim();
        return t.Length <= TitleMax ? t : t[..(TitleMax - 1)].TrimEnd() + "…";
    }

    private static IEnumerable<Row> Lines(string text, LineLevel level) =>
        (text ?? "")
            .Replace("\r", "")
            .Split(new[] { ". ", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => (Row)new LineRow(line.EndsWith('.') ? line : line + ".", level));
}
