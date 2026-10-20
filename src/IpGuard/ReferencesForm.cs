namespace IpGuard;

public sealed class ReferencesForm : Form
{
    private readonly MonitorState state;
    private readonly Func<IpInfo?> current;
    private readonly Func<string> home;
    private readonly Action changed;
    private readonly Func<string, string, string, string?> prompt;
    private readonly Func<string, bool> confirm;
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
        HideSelection = false
    };
    private readonly Button add = new() { Text = "Добавить текущий IP", AutoSize = true };
    private readonly Button rename = new() { Text = "Подпись...", AutoSize = true };
    private readonly Button delete = new() { Text = "Удалить", AutoSize = true };
    private readonly Button close = new() { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Label hint = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 8, 10, 0) };

    public ReferencesForm(
        MonitorState state,
        Func<IpInfo?> current,
        Func<string> home,
        Action changed,
        Func<string, string, string, string?>? prompt = null,
        Func<string, bool>? confirm = null)
    {
        this.state = state;
        this.current = current;
        this.home = home;
        this.changed = changed;
        this.prompt = prompt ?? PromptForm.Ask;
        this.confirm = confirm ?? (text => MessageBox.Show(text, "IpGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);

        Text = "Эталонные IP";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 360);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        CancelButton = close;

        list.Columns.Add("Страна", 160);
        list.Columns.Add("IP", 170);
        list.Columns.Add("Подпись", 280);
        list.SelectedIndexChanged += (_, _) => UpdateButtons();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(10, 10, 10, 8)
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(rename);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(close);

        add.Click += (_, _) => AddCurrent();
        rename.Click += (_, _) => RenameSelected();
        delete.Click += (_, _) => DeleteSelected();

        Controls.Add(list);
        Controls.Add(hint);
        Controls.Add(buttons);

        Refresh(null);
    }

    public IReadOnlyList<(string Country, string Ip, string Label)> Rows =>
        list.Items.Cast<ListViewItem>().Select(item => ((string)item.Tag!, item.SubItems[1].Text, item.SubItems[2].Text)).ToList();

    public bool AddEnabled => add.Enabled;

    public bool EditEnabled => rename.Enabled && delete.Enabled;

    public void Select(int index)
    {
        if (index >= 0 && index < list.Items.Count)
        {
            list.Items[index].Selected = true;
        }

        UpdateButtons();
    }

    public bool AddCurrent()
    {
        var ip = current();
        if (!CanAdd(ip))
        {
            return false;
        }

        var label = prompt("Подпись", $"Подпись для {ip!.Ip} ({ip.CountryCode}). Можно оставить пустой, например «основной» или «резерв».", "");
        if (label is null)
        {
            return false;
        }

        var added = state.AddReference(new Reference(ip.CountryCode, ip.Ip, label.Trim()));
        if (added)
        {
            changed();
            Refresh(ip.CountryCode + "|" + ip.Ip);
        }

        return added;
    }

    public bool RenameSelected()
    {
        if (Selected() is not { } reference)
        {
            return false;
        }

        var label = prompt("Подпись", $"Подпись для {reference.Ip} ({reference.Country}):", reference.Label);
        if (label is null || !state.RenameReference(reference.Country, reference.Ip, label))
        {
            return false;
        }

        changed();
        Refresh(reference.Country + "|" + reference.Ip);
        return true;
    }

    public bool DeleteSelected()
    {
        if (Selected() is not { } reference)
        {
            return false;
        }

        var text = $"Удалить эталон {reference.Ip} ({reference.Country})?";
        if (!confirm(text) || !state.RemoveReference(reference.Country, reference.Ip))
        {
            return false;
        }

        changed();
        Refresh(null);
        return true;
    }

    private bool CanAdd(IpInfo? ip) =>
        ip is not null
        && ip.CountryCode != home()
        && !state.ReferencesFor(ip.CountryCode).Any(reference => reference.Ip == ip.Ip);

    private Reference? Selected()
    {
        if (list.SelectedItems.Count == 0)
        {
            return null;
        }

        var item = list.SelectedItems[0];
        return new Reference((string)item.Tag!, item.SubItems[1].Text, item.SubItems[2].Text);
    }

    private void Refresh(string? keep)
    {
        state.MigrateLegacy();
        list.Items.Clear();
        foreach (var reference in state.References.OrderBy(r => r.Country, StringComparer.Ordinal))
        {
            var item = new ListViewItem(new[] { Countries.Describe(reference.Country), reference.Ip, reference.Label }) { Tag = reference.Country };
            list.Items.Add(item);
            if (keep == reference.Country + "|" + reference.Ip)
            {
                item.Selected = true;
            }
        }

        hint.Text = state.References.Count == 0
            ? "Эталонов пока нет. Подключитесь к нужному серверу и нажмите «Добавить текущий IP»."
            : "IP считается верным, если совпадает с любым эталоном своей страны. Для страны можно сохранить несколько адресов, например основной и резервный сервер.";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        add.Enabled = CanAdd(current());
        var selected = list.SelectedItems.Count > 0;
        rename.Enabled = selected;
        delete.Enabled = selected;
    }
}
