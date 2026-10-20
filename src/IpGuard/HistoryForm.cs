namespace IpGuard;

public sealed class HistoryForm : Form
{
    private readonly MonitorState state;
    private readonly Func<string> home;
    private readonly Action changed;
    private readonly Func<string?> pickSavePath;
    private readonly Func<string, bool> confirm;
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0) };
    private readonly ComboBox country = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox search = new() { Width = 200 };
    private readonly Label count = new() { AutoSize = true, Padding = new Padding(0, 6, 12, 0) };
    private readonly Button export = new() { Text = "Экспорт в CSV...", AutoSize = true };
    private readonly Button clear = new() { Text = "Очистить историю...", AutoSize = true };
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true
    };
    private bool loading;

    public HistoryForm(
        MonitorState state,
        Func<string> home,
        Action changed,
        Func<string?>? pickSavePath = null,
        Func<string, bool>? confirm = null)
    {
        this.state = state;
        this.home = home;
        this.changed = changed;
        this.pickSavePath = pickSavePath ?? AskSavePath;
        this.confirm = confirm ?? (text => MessageBox.Show(text, "IpGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);

        Text = "История IP";
        Size = new Size(1180, 560);
        StartPosition = FormStartPosition.CenterScreen;

        list.Columns.Add("Время", 140);
        list.Columns.Add("IP", 130);
        list.Columns.Add("Страна", 180);
        list.Columns.Add("Провайдер", 220);
        list.Columns.Add("Статус", 150);
        list.Columns.Add("Примечание", 360);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 8, 8, 0)
        };
        filters.Controls.Add(new Label { Text = "Страна", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        filters.Controls.Add(country);
        filters.Controls.Add(new Label { Text = "Статус", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        filters.Controls.Add(status);
        filters.Controls.Add(new Label { Text = "Поиск", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        filters.Controls.Add(search);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 8, 8, 6)
        };
        bottom.Controls.Add(count);
        bottom.Controls.Add(export);
        bottom.Controls.Add(clear);

        Controls.Add(list);
        Controls.Add(bottom);
        Controls.Add(filters);
        Controls.Add(summary);

        country.SelectedIndexChanged += (_, _) => Reload();
        status.SelectedIndexChanged += (_, _) => Reload();
        search.TextChanged += (_, _) => Reload();
        export.Click += (_, _) => ExportCsv();
        clear.Click += (_, _) => ClearHistory();

        Reload();
    }

    public IReadOnlyList<(string Ip, string CountryCode, Status Status, string Note)> Rows =>
        list.Items.Cast<ListViewItem>().Select(item => ((HistoryEntry)item.Tag!)).Select(e => (e.Ip, e.CountryCode, e.Status, e.Note ?? "")).ToList();

    public int ShownCount => list.Items.Count;

    public int TotalCount => state.History.Count;

    public string CountText => count.Text;

    public string SummaryText => summary.Text;

    public IReadOnlyList<string> CountryChoices => country.Items.Cast<object>().Select(item => item.ToString() ?? "").ToList();

    public IReadOnlyList<string> StatusChoices => status.Items.Cast<object>().Select(item => item.ToString() ?? "").ToList();

    public void Filter(string? countryCode, Status? statusFilter, string? text)
    {
        loading = true;
        try
        {
            FillCombos();
            Select(country, item => ((Choice<string>)item).Value == countryCode);
            Select(status, item => ((Choice<Status?>)item).Value == statusFilter);
            search.Text = text ?? "";
        }
        finally
        {
            loading = false;
        }

        Reload();
    }

    public string? ExportCsv()
    {
        var shown = CurrentEntries();
        if (shown.Count == 0)
        {
            return null;
        }

        var path = pickSavePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        HistoryFilter.Save(path, shown);
        return path;
    }

    public bool ClearHistory()
    {
        if (state.History.Count == 0 || !confirm($"Удалить всю историю ({state.History.Count} записей)? Эталоны останутся."))
        {
            return false;
        }

        state.History.Clear();
        changed();
        Reload();
        return true;
    }

    public void Reload()
    {
        if (loading)
        {
            return;
        }

        loading = true;
        try
        {
            FillCombos();
            state.MigrateLegacy();
            var references = state.References.Count == 0
                ? "Эталонных IP пока нет"
                : "Эталонные IP: " + string.Join(";  ", state.References.Select(r => $"{r.Country} — {IpWatcher.DescribeReferences(new[] { r })}"));
            summary.Text = $"Домашняя страна: {home()}\n{references}";

            list.BeginUpdate();
            list.Items.Clear();
            foreach (var entry in CurrentEntries())
            {
                list.Items.Add(new ListViewItem(new[]
                {
                    entry.Time.ToString("dd.MM.yyyy HH:mm:ss"),
                    entry.Ip,
                    entry.Country.Length > 0 ? $"{entry.Country} ({entry.CountryCode})" : entry.CountryCode,
                    entry.Provider,
                    Labels.For(entry.Status),
                    entry.Note?.Replace("\n", "; ") ?? ""
                }) { Tag = entry });
            }

            list.EndUpdate();
            count.Text = list.Items.Count == state.History.Count
                ? $"Записей: {state.History.Count}"
                : $"Показано {list.Items.Count} из {state.History.Count}";
            export.Enabled = list.Items.Count > 0;
            clear.Enabled = state.History.Count > 0;
        }
        finally
        {
            loading = false;
        }
    }

    private IReadOnlyList<HistoryEntry> CurrentEntries() =>
        HistoryFilter.Apply(
            state.History,
            (country.SelectedItem as Choice<string>)?.Value,
            (status.SelectedItem as Choice<Status?>)?.Value,
            search.Text);

    private void FillCombos()
    {
        var selectedCountry = (country.SelectedItem as Choice<string>)?.Value;
        var selectedStatus = (status.SelectedItem as Choice<Status?>)?.Value;

        country.Items.Clear();
        country.Items.Add(new Choice<string>(null, "Все страны"));
        foreach (var code in HistoryFilter.CountriesIn(state.History))
        {
            country.Items.Add(new Choice<string>(code, Countries.Describe(code)));
        }

        status.Items.Clear();
        status.Items.Add(new Choice<Status?>(null, "Все статусы"));
        foreach (var value in HistoryFilter.StatusesIn(state.History))
        {
            status.Items.Add(new Choice<Status?>(value, Labels.For(value)));
        }

        Select(country, item => ((Choice<string>)item).Value == selectedCountry);
        Select(status, item => ((Choice<Status?>)item).Value == selectedStatus);
    }

    private static void Select(ComboBox box, Func<object, bool> match)
    {
        var index = box.Items.Cast<object>().ToList().FindIndex(item => match(item));
        box.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string? AskSavePath()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт истории",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"ipguard-history-{DateTime.Now:yyyyMMdd}.csv"
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    private sealed record Choice<T>(T? Value, string Title)
    {
        public override string ToString() => Title;
    }
}
