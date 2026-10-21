namespace IpGuard;

public sealed class ReferencesPage : Page
{
    private const string EmptyHint = "Эталонов пока нет. Подключитесь к нужному серверу и нажмите «Добавить текущий IP».";
    private const string RuleHint = "IP считается верным, если совпадает с любым эталоном своей страны. Для страны можно сохранить несколько адресов, например основной и резервный сервер.";

    private readonly PanelServices services;

    public ReferencesPage(PanelServices services)
        : base("Эталоны")
    {
        this.services = services;
        Footer = new[] { new FooterButton("Закрыть", false, () => Host?.Pop()) };
        Build();
    }

    public override void OnShown() => Build();

    private bool CanAdd(IpInfo? ip, MonitorState state) =>
        ip is not null
        && ip.CountryCode != services.GetHome()
        && !state.ReferencesFor(ip.CountryCode).Any(reference => reference.Ip == ip.Ip);

    private void Build()
    {
        var state = services.GetState();
        state.MigrateLegacy();
        var ip = services.GetCurrentIp();
        var scroll = Rows.ScrollY;
        var rows = new List<Row>
        {
            new NoteRow(state.References.Count == 0 ? EmptyHint : RuleHint),
            new ButtonRow("Добавить текущий IP") { Id = "add", Primary = true, Enabled = CanAdd(ip, state), Action = () => Add(ip!) }
        };
        foreach (var reference in state.References.OrderBy(r => r.Country, StringComparer.Ordinal))
        {
            var current = reference;
            var place = Countries.Describe(reference.Country);
            rows.Add(new ItemRow(reference.Ip, reference.Label.Length == 0 ? place : $"{place} · {reference.Label}")
            {
                Id = "ref-" + reference.Country + "-" + reference.Ip,
                ShowEdit = true,
                ShowRemove = true,
                EditAction = () => Rename(current),
                RemoveAction = () => Remove(current)
            });
        }

        Rows.Replace(rows);
        Rows.ScrollBy(scroll);
    }

    private void Add(IpInfo ip) => Host?.Push(new PromptPage(
        "Подпись",
        $"Подпись для {ip.Ip} ({ip.CountryCode}). Можно оставить пустой, например «основной» или «резерв»:",
        "",
        40,
        null,
        label =>
        {
            if (services.GetState().AddReference(new Reference(ip.CountryCode, ip.Ip, label.Trim())))
            {
                services.ReferencesChanged();
            }
        }));

    private void Rename(Reference reference) => Host?.Push(new PromptPage(
        "Подпись",
        $"Подпись для {reference.Ip} ({reference.Country}):",
        reference.Label,
        40,
        null,
        label =>
        {
            if (services.GetState().RenameReference(reference.Country, reference.Ip, label))
            {
                services.ReferencesChanged();
            }
        }));

    private void Remove(Reference reference) => Host?.Push(new ConfirmPage(
        "Удалить эталон",
        $"Удалить эталон {reference.Ip} ({reference.Country})?",
        "Удалить",
        () =>
        {
            if (services.GetState().RemoveReference(reference.Country, reference.Ip))
            {
                services.ReferencesChanged();
            }
        }));
}
