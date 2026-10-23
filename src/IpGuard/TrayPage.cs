namespace IpGuard;

public sealed class TrayPage : Page
{
    private static readonly int[] QuietMinutes = { 0, 15, 30, 60 };
    private static readonly string[] QuietTitles = { "Выключено", "15 минут", "30 минут", "60 минут" };

    private readonly PanelServices services;
    private readonly Func<PanelState> state;
    private string shown = "";
    private double lastPoll = -10;

    public TrayPage(PanelServices services, Func<PanelState> state)
        : base("IpGuard")
    {
        this.services = services;
        this.state = state;
        Footer = new[]
        {
            new FooterButton("Перезапустить", false, services.Restart),
            new FooterButton("Выход", true, services.Exit)
        };
        Build();
    }

    public override void OnShown() => Build();

    public override void OnTick(double now)
    {
        if (now - lastPoll < 1.0)
        {
            return;
        }

        lastPoll = now;
        if (Snapshot() != shown)
        {
            Build();
        }
    }

    private string Snapshot()
    {
        var s = state();
        return string.Join("|", s.Label, s.Note, Address(), string.Join(";", services.GetRoutes()), services.GetQuietUntil()?.ToString("HH:mm") ?? "", services.GetUpdateTag() ?? "");
    }

    private string Address()
    {
        var ip = services.GetCurrentIp();
        return ip is null ? "Нет связи" : $"{ip.Ip} · {ip.Country} · {ip.Provider}";
    }

    private void Build()
    {
        shown = Snapshot();
        var s = state();
        var rows = new List<Row>
        {
            new SectionRow("Сейчас"),
            new LineRow(s.Label, LineRow.LevelOf(s.Label)),
            new NoteRow(Address())
        };
        foreach (var route in services.GetRoutes())
        {
            rows.Add(new NoteRow(route));
        }

        if (!string.IsNullOrWhiteSpace(s.Note))
        {
            rows.Add(new NoteRow(s.Note));
        }

        var until = services.GetQuietUntil();
        var choice = new ChoiceRow("Не беспокоить", QuietTitles, until is null ? 0 : NearestQuiet(until.Value)) { Id = "quiet" };
        choice.Changed = () => services.SetQuiet(QuietMinutes[choice.Index]);
        rows.Add(new SectionRow("Не беспокоить"));
        rows.Add(choice);
        if (until is { } end)
        {
            rows.Add(new NoteRow($"Тревоги без звука до {end:HH:mm}."));
        }

        rows.Add(new SectionRow("Программа"));
        rows.Add(new NoteRow($"Версия {services.Version()}"));
        rows.Add(new ButtonRow("Проверить обновления") { Id = "check-updates", Action = services.CheckUpdates });
        if (services.GetUpdateTag() is { Length: > 0 } tag)
        {
            rows.Add(new ButtonRow($"Доступна версия {tag}: открыть страницу") { Id = "open-update", Primary = true, Action = services.OpenUpdate });
        }

        var scroll = Rows.ScrollY;
        Rows.Replace(rows);
        Rows.ScrollBy(scroll);
    }

    private static int NearestQuiet(DateTime until)
    {
        var left = (until - DateTime.Now).TotalMinutes;
        return left <= 0 ? 0 : left <= 15 ? 1 : left <= 30 ? 2 : 3;
    }
}
