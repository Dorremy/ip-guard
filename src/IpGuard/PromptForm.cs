namespace IpGuard;

public sealed class PromptForm : Form
{
    private readonly TextBox box = new() { Dock = DockStyle.Top };

    public PromptForm(string title, string caption, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 120);

        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 0, 0, 6) };
        box.Text = initial;
        box.MaxLength = 40;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0) };
        body.Controls.Add(box);
        body.Controls.Add(label);

        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => box.Text.Trim();

    public static string? Ask(string title, string caption, string initial)
    {
        using var form = new PromptForm(title, caption, initial);
        return form.ShowDialog() == DialogResult.OK ? form.Value : null;
    }
}
