namespace IpGuard;

public sealed class TextPage : Page
{
    private TextPage(string title, string text, Func<TextPage, IReadOnlyList<FooterButton>> footer)
        : base(title)
    {
        Rows.Replace((text ?? "")
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => (Row)new NoteRow(line)));
        Footer = footer(this);
    }

    public static TextPage Notice(string title, string text) =>
        new(title, text, page => new[] { new FooterButton("Понятно", true, () => page.Host?.Pop()) });

    public static TextPage Confirm(string title, string text, string okText, Action accepted) =>
        new(title, text, page => new[]
        {
            new FooterButton("Отмена", false, () => page.Host?.Pop()),
            new FooterButton(okText, true, () =>
            {
                accepted();
                page.Host?.Pop();
            })
        });
}
