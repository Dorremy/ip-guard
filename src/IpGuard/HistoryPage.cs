namespace IpGuard;

public sealed class HistoryPage : Page
{
    public const int RowLimit = 1000;
    private const string AllCountries = "Все страны";
    private const string AllStatuses = "Все статусы";

    private readonly PanelServices services;
    private readonly TextRow search = new("Поиск: IP, провайдер, примечание", "", 60) { Id = "search" };
    private readonly NoteRow message = new("") { Id = "message" };
    private readonly NoteRow count = new("") { Id = "count" };
    private ChoiceRow? country;
    private ChoiceRow? status;
    private IReadOnlyList<string> countryCodes = Array.Empty<string>();
    private IReadOnlyList<Status> statusValues = Array.Empty<Status>();
    private string? countryFilter;
    private Status? statusFilter;
    private string searchText = "";
    private int lastStamp;
    private double lastPoll = -10;

    public HistoryPage(PanelServices services)
        : base("История")
    {
        this.services = services;
        Footer = new[]
        {
            new FooterButton("Очистить…", false, Clear, () => TotalCount > 0),
            new FooterButton("Экспорт CSV", true, Export, () => ShownCount > 0)
        };
        lastStamp = Stamp();
        Reload(false);
    }

    public int ShownCount { get; private set; }

    public int TotalCount { get; private set; }

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
        var stamp = Stamp();
        if (stamp != lastStamp)
        {
            lastStamp = stamp;
            Reload(true);
        }
    }

    private int Stamp() => HashCode.Combine(services.HistoryStamp(), services.GetState().References.Count);

    private IReadOnlyList<HistoryEntry> Current() =>
        HistoryFilter.Apply(services.GetHistory(), countryFilter, statusFilter, searchText);

    private void Reload(bool keepView)
    {
        var history = services.GetHistory();
        TotalCount = history.Count;
        countryCodes = HistoryFilter.CountriesIn(history);
        statusValues = HistoryFilter.StatusesIn(history);
        if (countryFilter is not null && !countryCodes.Contains(countryFilter))
        {
            countryFilter = null;
        }

        if (statusFilter is { } wanted && !statusValues.Contains(wanted))
        {
            statusFilter = null;
        }

        country = Filter(country, "country", "Страна", new[] { AllCountries }.Concat(countryCodes.Select(Countries.Describe)).ToArray(), countryFilter is null ? 0 : countryCodes.ToList().IndexOf(countryFilter) + 1, index =>
        {
            countryFilter = index == 0 ? null : countryCodes[index - 1];
            Reload(false);
        });
        status = Filter(status, "status", "Статус", new[] { AllStatuses }.Concat(statusValues.Select(Labels.For)).ToArray(), statusFilter is null ? 0 : statusValues.ToList().IndexOf(statusFilter.Value) + 1, index =>
        {
            statusFilter = index == 0 ? null : statusValues[index - 1];
            Reload(false);
        });
        var entries = Current();
        ShownCount = entries.Count;
        count.SetText($"{CountText(entries.Count)} · дом {services.GetHome()}");
        var focusId = Rows.Focus >= 0 && Rows.Focus < Rows.Rows.Count ? Rows.Rows[Rows.Focus].Id : "";
        var scroll = Rows.ScrollY;
        var rows = new List<Row> { country, status, search, message, count };
        rows.AddRange(entries.Take(RowLimit).Select(entry => (Row)new HistoryRow(entry)));
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

    private ChoiceRow Filter(ChoiceRow? existing, string id, string label, string[] options, int selected, Action<int> changed)
    {
        if (existing is not null && existing.Options.SequenceEqual(options))
        {
            return existing;
        }

        var row = new ChoiceRow(label, options, selected) { Id = id };
        row.Changed = () => changed(row.Index);
        return row;
    }

    private string CountText(int shown)
    {
        if (TotalCount == 0)
        {
            return "Записей нет. Они появятся после первых проверок.";
        }

        var text = shown == TotalCount ? $"Записей: {TotalCount}" : $"Показано {shown} из {TotalCount}";
        return shown > RowLimit ? text + $" (на экране последние {RowLimit}, в CSV уйдёт всё)" : text;
    }


    private void Export()
    {
        var entries = Current();
        if (entries.Count == 0)
        {
            SetMessage("Нечего экспортировать.");
            return;
        }

        var path = services.PickSavePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            HistoryFilter.Save(path, entries);
            SetMessage($"Сохранено: {path} (записей: {entries.Count})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetMessage($"Не удалось сохранить файл: {e.Message}");
        }
    }

    private void Clear() => Host?.Push(new ConfirmPage(
        "История",
        $"Удалить всю историю ({TotalCount} записей)? Эталоны останутся.",
        "Удалить",
        () =>
        {
            services.ClearHistory();
            Reload(false);
        }));

    private void SetMessage(string text)
    {
        message.SetText(text);
        Rows.Refresh();
    }
}
