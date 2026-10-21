using System.Text;

namespace IpGuard;

public sealed class TextEditor
{
    private string text = "";

    private readonly Func<char, bool>? filter;

    public TextEditor(string? text = "", int maxLength = 200, Func<char, bool>? filter = null)
    {
        MaxLength = Math.Max(0, maxLength);
        this.filter = filter;
        SetText(text);
    }

    public int MaxLength { get; }

    public string Text => text;

    public int Caret { get; private set; }

    public int Anchor { get; private set; }

    public int Version { get; private set; }

    public bool HasSelection => Caret != Anchor;

    public int SelectionStart => Math.Min(Caret, Anchor);

    public int SelectionLength => Math.Abs(Caret - Anchor);

    public string SelectedText => text.Substring(SelectionStart, SelectionLength);

    public void SetText(string? value)
    {
        var clean = Sanitize(value);
        text = clean.Length > MaxLength ? clean[..MaxLength] : clean;
        Caret = Anchor = text.Length;
        Version++;
    }

    public void Insert(string? value)
    {
        var clean = Sanitize(value);
        if (clean.Length == 0 && !HasSelection)
        {
            return;
        }

        var start = SelectionStart;
        var length = SelectionLength;
        var room = MaxLength - (text.Length - length);
        if (room <= 0)
        {
            if (length > 0)
            {
                Remove(start, length);
            }

            return;
        }

        if (clean.Length > room)
        {
            clean = clean[..room];
            if (clean.Length > 0 && char.IsHighSurrogate(clean[^1]))
            {
                clean = clean[..^1];
            }
        }

        text = text.Remove(start, length).Insert(start, clean);
        Caret = Anchor = start + clean.Length;
        Version++;
    }

    public void Backspace()
    {
        if (HasSelection)
        {
            Remove(SelectionStart, SelectionLength);
            return;
        }

        if (Caret == 0)
        {
            return;
        }

        var count = Caret >= 2 && char.IsLowSurrogate(text[Caret - 1]) && char.IsHighSurrogate(text[Caret - 2]) ? 2 : 1;
        Remove(Caret - count, count);
    }

    public void Delete()
    {
        if (HasSelection)
        {
            Remove(SelectionStart, SelectionLength);
            return;
        }

        if (Caret >= text.Length)
        {
            return;
        }

        var count = char.IsHighSurrogate(text[Caret]) && Caret + 1 < text.Length && char.IsLowSurrogate(text[Caret + 1]) ? 2 : 1;
        Remove(Caret, count);
    }

    public void Move(int delta, bool select = false)
    {
        if (!select && HasSelection)
        {
            var edge = delta < 0 ? SelectionStart : SelectionStart + SelectionLength;
            Caret = Anchor = edge;
            Version++;
            return;
        }

        var target = Math.Clamp(Caret + delta, 0, text.Length);
        SetCaret(target, select);
    }

    public void Home(bool select = false) => SetCaret(0, select);

    public void End(bool select = false) => SetCaret(text.Length, select);

    public void SelectAll()
    {
        Anchor = 0;
        Caret = text.Length;
        Version++;
    }

    public void SetCaret(int index, bool select = false)
    {
        Caret = Math.Clamp(index, 0, text.Length);
        if (!select)
        {
            Anchor = Caret;
        }

        Version++;
    }

    public string CutSelection()
    {
        if (!HasSelection)
        {
            return "";
        }

        var cut = SelectedText;
        Remove(SelectionStart, SelectionLength);
        return cut;
    }

    private void Remove(int start, int count)
    {
        text = text.Remove(start, count);
        Caret = Anchor = start;
        Version++;
    }

    private string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append(' ');
            }
            else if (c == '\t')
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(c) && (filter is null || filter(c)))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
