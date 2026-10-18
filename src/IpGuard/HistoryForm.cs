namespace IpGuard;

public sealed class HistoryForm : Form
{
    public HistoryForm(MonitorState state, string home)
    {
        Text = "История IP";
        Size = new Size(860, 480);
        StartPosition = FormStartPosition.CenterScreen;

        var trusted = state.Trusted.Count == 0
            ? "Эталонных IP пока нет"
            : "Эталонные IP: " + string.Join(";  ", state.Trusted.Select(p => $"{p.Key} — {p.Value}"));
        var summary = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8, 8, 8, 0),
            Text = $"Домашняя страна: {home}\n{trusted}"
        };

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        list.Columns.Add("Время", 140);
        list.Columns.Add("IP", 130);
        list.Columns.Add("Страна", 180);
        list.Columns.Add("Провайдер", 220);
        list.Columns.Add("Статус", 150);

        foreach (var entry in Enumerable.Reverse(state.History))
        {
            list.Items.Add(new ListViewItem(new[]
            {
                entry.Time.ToString("dd.MM.yyyy HH:mm:ss"),
                entry.Ip,
                $"{entry.Country} ({entry.CountryCode})",
                entry.Provider,
                Labels.For(entry.Status)
            }));
        }

        Controls.Add(list);
        Controls.Add(summary);
    }
}
