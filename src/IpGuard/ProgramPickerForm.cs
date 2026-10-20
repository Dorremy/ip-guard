namespace IpGuard;

public sealed class ProgramPickerForm : Form
{
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };
    private readonly Button ok = new() { Text = "Добавить", AutoSize = true, DialogResult = DialogResult.OK, Enabled = false };
    private readonly Button cancel = new() { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };

    public ProgramPickerForm(IReadOnlyList<RunningProgram> programs)
    {
        Text = "Запущенные программы";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 380);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 8, 10, 0),
            Text = "Выберите программу, которую нужно закрывать при тревоге. Показаны программы с открытыми окнами."
        };

        list.Columns.Add("Программа", 170);
        list.Columns.Add("Окно", 360);
        foreach (var program in programs)
        {
            list.Items.Add(new ListViewItem(new[] { program.Name, program.Title }) { Tag = program.Name });
        }

        list.SelectedIndexChanged += (_, _) => ok.Enabled = list.SelectedItems.Count > 0;
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItems.Count > 0)
            {
                DialogResult = DialogResult.OK;
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 8, 10, 6)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(list);
        Controls.Add(buttons);
        Controls.Add(hint);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string? Selected => list.SelectedItems.Count > 0 ? (string?)list.SelectedItems[0].Tag : null;

    public static string? Pick(IReadOnlyList<RunningProgram>? programs = null)
    {
        using var form = new ProgramPickerForm(programs ?? ProgramCloser.RunningWithWindows());
        return form.ShowDialog() == DialogResult.OK ? form.Selected : null;
    }
}
