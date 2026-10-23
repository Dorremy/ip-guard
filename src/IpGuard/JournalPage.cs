namespace IpGuard;

public sealed class JournalPage : Page
{
    public const int LineLimit = 500;

    private readonly PanelServices services;
    private readonly TextRow search = new("Поиск по журналу", "", 60) { Id = "search" };
    private readonly NoteRow message = new("") { Id = "message" };
    private readonly NoteRow count = new("") { Id = "count" };
    private IReadOnlyList<string> shown = Array.Empty<string>();
    private string searchText = "";
    private long lastStamp;
    private double lastPoll = -10;

    public JournalPage(PanelServices services)
        : base("Журнал")
    {
        this.services = services;
        Footer = new[]
        {
            new FooterButton("Открыть файл", false, () => services.OpenJournalFile()),
            new FooterButton("Копировать", true, Copy, () => shown.Count > 0)
        };
        lastStamp = services.JournalStamp();
        Reload(false);
    }

    public override void OnShown() => Reload(true);

    public override void OnInput()
    {
        var text = search.Editor.Text.Trim();
        if (!string.Equals(text, searchText, StringComparison.Ordinal))
        {
            searchText = text;
            Reload(false);
        }
    }

    public override void OnTick(double now)
    {
        if (now - lastPoll < 1.0)
        {
            return;
        }

        lastPoll = now;
        var stamp = services.JournalStamp();
        if (stamp != lastStamp)
        {
            lastStamp = stamp;
            Reload(true);
        }
    }

    private void Reload(bool keepView)
    {
        var all = services.ReadJournal();
        var matching = all
            .Reverse()
            .Where(line => searchText.Length == 0 || line.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        shown = matching.Take(LineLimit).ToList();
        count.SetText(CountText(all.Count, matching.Count));
        var focusId = Rows.Focus >= 0 && Rows.Focus < Rows.Rows.Count ? Rows.Rows[Rows.Focus].Id : "";
        var scroll = Rows.ScrollY;
        var rows = new List<Row> { search, message, count };
        rows.AddRange(shown.Select(line => (Row)new LineRow(line, LineRow.LevelOf(line))));
        Rows.Replace(rows);
        if (focusId.Length > 0)
        {
            Rows.SetFocus(Rows.Rows.ToList().FindIndex(r => r.Id == focusId));
        }

        if (keepView)
        {
            Rows.ScrollBy(scroll);
        }
    }

    private static string CountText(int total, int matching)
    {
        if (total == 0)
        {
            return "Журнал пуст.";
        }

        var text = matching == total ? $"Строк: {total}" : $"Показано {matching} из {total}";
        return matching > LineLimit ? text + $" (на экране последние {LineLimit})" : text;
    }

    private void Copy()
    {
        if (shown.Count == 0)
        {
            return;
        }

        services.CopyText(string.Join(Environment.NewLine, shown));
        message.SetText($"Скопировано строк: {shown.Count}");
        Rows.Refresh();
    }
}
