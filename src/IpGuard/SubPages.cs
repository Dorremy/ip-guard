namespace IpGuard;

public sealed class PromptPage : Page
{
    private readonly Func<string, string?>? validate;
    private readonly Action<string> accepted;
    private readonly NoteRow errorRow = new("");

    public PromptPage(string title, string caption, string initial, int maxLength, Func<string, string?>? validate, Action<string> accepted, bool digitsOnly = false, string okText = "OK")
        : base(title)
    {
        this.validate = validate;
        this.accepted = accepted;
        Input = new TextRow("", initial, maxLength, digitsOnly ? char.IsDigit : null);
        Rows.Replace(new Row[] { new NoteRow(caption), Input, errorRow });
        Footer = new[]
        {
            new FooterButton("Отмена", false, () => Host?.Pop()),
            new FooterButton(okText, true, Submit)
        };
    }

    public TextRow Input { get; }

    public string? Error { get; private set; }

    public override void OnShown()
    {
        Rows.SetFocus(1);
        Input.Editor.SelectAll();
    }

    public void Submit()
    {
        var text = Input.Editor.Text;
        Error = validate?.Invoke(text);
        errorRow.SetText(Error ?? "");
        if (Error is not null)
        {
            Rows.Refresh();
            return;
        }

        accepted(text);
        Host?.Pop();
    }
}

public sealed class ConfirmPage : Page
{
    public ConfirmPage(string title, string text, string okText, Action accepted)
        : base(title)
    {
        Rows.Replace(new Row[] { new NoteRow(text) });
        Footer = new[]
        {
            new FooterButton("Отмена", false, () => Host?.Pop()),
            new FooterButton(okText, true, () =>
            {
                accepted();
                Host?.Pop();
            })
        };
    }
}

public abstract class ListPickerPage : Page
{
    private readonly NoteRow errorRow = new("");
    private string filter = "";

    protected ListPickerPage(string title)
        : base(title)
    {
        Search = new TextRow("Поиск", "", 40);
        Footer = new[] { new FooterButton("Отмена", false, () => Host?.Pop()) };
    }

    public TextRow Search { get; }

    protected abstract IEnumerable<ItemRow> Items(string filter);

    protected void ShowError(string? message)
    {
        errorRow.SetText(message ?? "");
        Rows.Refresh();
    }

    protected void Build()
    {
        var rows = new List<Row> { Search, errorRow };
        rows.AddRange(Items(filter));
        Rows.Replace(rows);
        Rows.SetFocus(0);
    }

    public override void OnShown()
    {
        if (Rows.Rows.Count == 0)
        {
            Build();
        }

        Rows.SetFocus(0);
    }

    public override void OnInput()
    {
        var text = Search.Editor.Text.Trim();
        if (!string.Equals(text, filter, StringComparison.Ordinal))
        {
            filter = text;
            Build();
        }
    }

    protected static bool Matches(string filter, params string[] fields) =>
        filter.Length == 0 || fields.Any(field => field.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
}

public sealed class CountryPickerPage : ListPickerPage
{
    private readonly string current;
    private readonly Action<string> picked;

    public CountryPickerPage(string current, Action<string> picked)
        : base("Страна")
    {
        this.current = current;
        this.picked = picked;
        Build();
    }

    protected override IEnumerable<ItemRow> Items(string filter)
    {
        foreach (var (code, title) in Countries.Choices(current))
        {
            if (!Matches(filter, title, code))
            {
                continue;
            }

            var chosen = code;
            yield return new ItemRow(title, "") { Marked = code == current, Open = () =>
            {
                picked(chosen);
                Host?.Pop();
            } };
        }
    }
}

public sealed class ProgramPickerPage : ListPickerPage
{
    private readonly IReadOnlyList<RunningProgram> programs;
    private readonly Func<string, string?> pick;

    public ProgramPickerPage(IReadOnlyList<RunningProgram> programs, Func<string, string?> pick)
        : base("Запущенные программы")
    {
        this.programs = programs;
        this.pick = pick;
        Build();
    }

    protected override IEnumerable<ItemRow> Items(string filter)
    {
        foreach (var program in programs)
        {
            if (!Matches(filter, program.Name, program.Title))
            {
                continue;
            }

            var name = program.Name;
            yield return new ItemRow(program.Name, program.Title) { Open = () =>
            {
                var error = pick(name);
                if (error is null)
                {
                    Host?.Pop();
                }
                else
                {
                    ShowError(error);
                }
            } };
        }
    }
}
