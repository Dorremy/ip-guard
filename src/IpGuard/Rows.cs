namespace IpGuard;

public enum RowPart
{
    None,
    Body,
    Minus,
    Plus,
    Value,
    Prev,
    Next,
    Track,
    Play,
    Edit,
    Remove
}

public static class RowMetrics
{
    public const float Padding = 12f;
    public const float Gap = 2f;
    public const float SectionHeight = 28f;
    public const float RowHeight = 36f;
    public const float HintExtra = 14f;
    public const float SliderHeight = 48f;
    public const float ItemHeight = 44f;
    public const float TextHeight = 40f;
    public const float NoteLine = 16f;
    public const float SwitchWidth = 42f;
    public const float SwitchHeight = 22f;
    public const float StepperWidth = 112f;
    public const float StepButton = 28f;
    public const float ChoiceWidth = 150f;
    public const float ChoiceArrow = 26f;
    public const float PlayWidth = 28f;
    public const float TwoLineHeight = 62f;
    public const float ControlLine = 28f;
    public const float IconButton = 28f;
    public const float IconGap = 4f;
    public const float NoteFont = 12f;
    public const float HistoryHeight = 52f;
    public const int LineMaxLines = 4;
    public const float LabelFont = 14f;
    public const float LabelLine = 19f;
    public const float HintLine = 15f;
    public const int LabelMaxLines = 2;
    public const int HintMaxLines = 3;
    public const float ToggleTextGap = 10f;
}

public static class TextMetrics
{
    public static float Width(string text, float fontPx)
    {
        var sum = 0f;
        foreach (var c in text)
        {
            sum += c == ' ' ? 0.3f : 0.58f;
        }

        return sum * fontPx;
    }

    public static int Lines(string text, float width, float fontPx)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var limit = Math.Max(fontPx * 2, width);
        var lines = 1;
        var current = 0f;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = Width(word, fontPx);
            var space = current == 0 ? 0 : Width(" ", fontPx);
            if (current + space + w <= limit)
            {
                current += space + w;
                continue;
            }

            if (current > 0)
            {
                lines++;
            }

            while (w > limit)
            {
                lines++;
                w -= limit;
            }

            current = w;
        }

        return lines;
    }
}

public abstract class Row
{
    public string Id { get; init; } = "";

    public Action? Changed { get; set; }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled != value)
            {
                enabled = value;
                Invalidate();
            }
        }
    }

    internal RowList? Owner { get; set; }

    private bool enabled = true;

    public virtual bool Focusable => Enabled;

    public virtual float Height(float width) => RowMetrics.RowHeight;

    public virtual RowPart HitPart(float x, float y, float width) => RowPart.Body;

    public virtual bool Click(RowPart part, float fraction) => false;

    public virtual bool Key(Keys key) => false;

    public void Invalidate() => Owner?.Touch();

    protected void Report()
    {
        Invalidate();
        Changed?.Invoke();
    }
}

public sealed class SectionRow : Row
{
    public SectionRow(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public override bool Focusable => false;

    public override float Height(float width) => RowMetrics.SectionHeight;
}

public sealed class NoteRow : Row
{
    public NoteRow(string text)
    {
        Text = text;
    }

    public string Text { get; private set; }

    public override bool Focusable => false;

    public override float Height(float width)
    {
        var lines = TextMetrics.Lines(Text, width - 2 * RowMetrics.Padding, RowMetrics.NoteFont);
        return lines == 0 ? 4f : lines * RowMetrics.NoteLine + 10f;
    }

    public void SetText(string text)
    {
        if (text != Text)
        {
            Text = text;
            Invalidate();
        }
    }
}

public sealed class ToggleRow : Row
{
    public ToggleRow(string label, bool value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string? Hint
    {
        get => hint;
        set
        {
            if (value != hint)
            {
                hint = value;
                Invalidate();
            }
        }
    }

    private string? hint;

    public bool Value { get; private set; }

    public static float TextWidth(float rowWidth)
    {
        var w = float.IsFinite(rowWidth) ? rowWidth : 0f;
        return Math.Max(60f, w - 2 * RowMetrics.Padding - RowMetrics.SwitchWidth - RowMetrics.ToggleTextGap);
    }

    public int LabelLines(float rowWidth) =>
        Math.Clamp(TextMetrics.Lines(Label, TextWidth(rowWidth), RowMetrics.LabelFont), 1, RowMetrics.LabelMaxLines);

    public int HintLines(float rowWidth) =>
        Hint is null ? 0 : Math.Clamp(TextMetrics.Lines(Hint, TextWidth(rowWidth), RowMetrics.NoteFont), 1, RowMetrics.HintMaxLines);

    public override float Height(float width)
    {
        var labelLines = LabelLines(width);
        var hintLines = HintLines(width);
        if (hintLines == 0)
        {
            return labelLines == 1 ? RowMetrics.RowHeight : 16f + labelLines * RowMetrics.LabelLine;
        }

        return Math.Max(RowMetrics.RowHeight + RowMetrics.HintExtra, 14f + labelLines * RowMetrics.LabelLine + hintLines * RowMetrics.HintLine + 2f);
    }

    public void Set(bool value)
    {
        if (value != Value)
        {
            Value = value;
            Report();
        }
    }

    public void Toggle()
    {
        if (Enabled)
        {
            Set(!Value);
        }
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled)
        {
            return false;
        }

        Toggle();
        return true;
    }

    public override bool Key(Keys key)
    {
        if (Enabled && key is Keys.Space or Keys.Enter)
        {
            Toggle();
            return true;
        }

        return false;
    }
}

public sealed class SliderRow : Row
{
    public SliderRow(string label, int min, int max, int step, int value)
    {
        Label = label;
        Min = min;
        Max = max;
        Step = Math.Max(1, step);
        Value = Math.Clamp(value, min, max);
    }

    public string Label { get; }

    public int Min { get; }

    public int Max { get; }

    public int Step { get; }

    public int Value { get; private set; }

    public override float Height(float width) => RowMetrics.SliderHeight;

    public override RowPart HitPart(float x, float y, float width) => y >= RowMetrics.SliderHeight / 2 ? RowPart.Track : RowPart.Body;

    public float FractionAt(float x, float width)
    {
        var span = width - 2 * RowMetrics.Padding;
        return span <= 0 ? 0f : (x - RowMetrics.Padding) / span;
    }

    public void SetValue(int value)
    {
        var clamped = Math.Clamp(value, Min, Max);
        if (clamped != Value)
        {
            Value = clamped;
            Report();
        }
    }

    public void SetFromFraction(float fraction)
    {
        if (!Enabled)
        {
            return;
        }

        var f = Math.Clamp(fraction, 0f, 1f);
        var raw = Min + f * (Max - Min);
        var snapped = Min + (int)MathF.Round((raw - Min) / Step) * Step;
        SetValue(snapped);
    }

    public void Nudge(int direction)
    {
        if (Enabled)
        {
            SetValue(Value + direction * Step);
        }
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled || part != RowPart.Track)
        {
            return false;
        }

        SetFromFraction(fraction);
        return true;
    }

    public override bool Key(Keys key)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (key)
        {
            case Keys.Left:
                Nudge(-1);
                return true;
            case Keys.Right:
                Nudge(1);
                return true;
            case Keys.Home:
                SetValue(Min);
                return true;
            case Keys.End:
                SetValue(Max);
                return true;
        }

        return false;
    }
}

public sealed class ChoiceRow : Row
{
    private readonly string[] options;

    public ChoiceRow(string label, string[] options, int index)
    {
        Label = label;
        this.options = options;
        Index = options.Length == 0 ? 0 : Math.Clamp(index, 0, options.Length - 1);
    }

    public string Label { get; }

    public IReadOnlyList<string> Options => options;

    public int Index { get; private set; }

    public bool ShowPlay { get; init; }

    public Action? PlayAction { get; set; }

    public string Selected => options.Length == 0 ? "" : options[Index];

    public override float Height(float width) => ShowPlay ? RowMetrics.TwoLineHeight : RowMetrics.RowHeight;

    public override RowPart HitPart(float x, float y, float width)
    {
        if (ShowPlay)
        {
            if (y < RowMetrics.ControlLine)
            {
                return RowPart.Body;
            }

            if (x >= RowMetrics.Padding && x < RowMetrics.Padding + RowMetrics.PlayWidth)
            {
                return RowPart.Play;
            }

            var prevLeft = RowMetrics.Padding + RowMetrics.PlayWidth + 6;
            if (x >= prevLeft && x < prevLeft + RowMetrics.ChoiceArrow)
            {
                return RowPart.Prev;
            }

            if (x >= width - RowMetrics.Padding - RowMetrics.ChoiceArrow && x <= width - RowMetrics.Padding)
            {
                return RowPart.Next;
            }

            return RowPart.Body;
        }

        var left = width - RowMetrics.Padding - RowMetrics.ChoiceWidth;
        if (x >= left && x < left + RowMetrics.ChoiceArrow)
        {
            return RowPart.Prev;
        }

        if (x >= width - RowMetrics.Padding - RowMetrics.ChoiceArrow && x <= width - RowMetrics.Padding)
        {
            return RowPart.Next;
        }

        return RowPart.Body;
    }

    public void Select(int index)
    {
        if (options.Length == 0)
        {
            return;
        }

        var clamped = Math.Clamp(index, 0, options.Length - 1);
        if (clamped != Index)
        {
            Index = clamped;
            Report();
        }
    }

    public void Next()
    {
        if (options.Length > 0 && Enabled)
        {
            Index = (Index + 1) % options.Length;
            Report();
        }
    }

    public void Prev()
    {
        if (options.Length > 0 && Enabled)
        {
            Index = (Index - 1 + options.Length) % options.Length;
            Report();
        }
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (part)
        {
            case RowPart.Prev:
                Prev();
                return true;
            case RowPart.Play:
                PlayAction?.Invoke();
                return true;
            case RowPart.Next:
            case RowPart.Body:
                Next();
                return true;
        }

        return false;
    }

    public override bool Key(Keys key)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (key)
        {
            case Keys.Left:
                Prev();
                return true;
            case Keys.Right:
            case Keys.Enter:
            case Keys.Space:
                Next();
                return true;
        }

        return false;
    }
}

public sealed class StepperRow : Row
{
    public StepperRow(string label, int min, int max, int step, int value)
    {
        Label = label;
        Min = min;
        Max = max;
        Step = Math.Max(1, step);
        Value = Math.Clamp(value, min, max);
    }

    public string Label { get; }

    public int Min { get; }

    public int Max { get; }

    public int Step { get; }

    public int Value { get; private set; }

    public string Unit { get; init; } = "";

    public Action? Prompt { get; set; }

    public override RowPart HitPart(float x, float y, float width)
    {
        var left = width - RowMetrics.Padding - RowMetrics.StepperWidth;
        if (x >= left && x < left + RowMetrics.StepButton)
        {
            return RowPart.Minus;
        }

        if (x >= width - RowMetrics.Padding - RowMetrics.StepButton && x <= width - RowMetrics.Padding)
        {
            return RowPart.Plus;
        }

        if (x >= left + RowMetrics.StepButton && x < width - RowMetrics.Padding - RowMetrics.StepButton)
        {
            return RowPart.Value;
        }

        return RowPart.Body;
    }

    public void SetValue(int value)
    {
        var clamped = Math.Clamp(value, Min, Max);
        if (clamped != Value)
        {
            Value = clamped;
            Report();
        }
    }

    public void Plus(int multiplier = 1)
    {
        if (Enabled)
        {
            SetValue(Value + Step * Math.Max(1, multiplier));
        }
    }

    public void Minus(int multiplier = 1)
    {
        if (Enabled)
        {
            SetValue(Value - Step * Math.Max(1, multiplier));
        }
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (part)
        {
            case RowPart.Minus:
                Minus();
                return true;
            case RowPart.Plus:
                Plus();
                return true;
            case RowPart.Value:
                Prompt?.Invoke();
                return true;
        }

        return false;
    }

    public override bool Key(Keys key)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (key)
        {
            case Keys.Left:
                Minus();
                return true;
            case Keys.Right:
                Plus();
                return true;
            case Keys.Enter:
                Prompt?.Invoke();
                return true;
        }

        return false;
    }
}

public sealed class PickerRow : Row
{
    public PickerRow(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; private set; }

    public Action? Open { get; set; }

    public void SetValue(string value)
    {
        if (value != Value)
        {
            Value = value;
            Invalidate();
        }
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled)
        {
            return false;
        }

        Open?.Invoke();
        return true;
    }

    public override bool Key(Keys key)
    {
        if (Enabled && key is Keys.Enter or Keys.Space)
        {
            Open?.Invoke();
            return true;
        }

        return false;
    }
}

public sealed class ButtonRow : Row
{
    public ButtonRow(string text)
    {
        Text = text;
    }

    public string Text { get; private set; }

    public bool Primary { get; init; }

    public Action? Action { get; set; }

    public void SetText(string text)
    {
        if (text != Text)
        {
            Text = text;
            Invalidate();
        }
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled)
        {
            return false;
        }

        Action?.Invoke();
        return true;
    }

    public override bool Key(Keys key)
    {
        if (Enabled && key is Keys.Enter or Keys.Space)
        {
            Action?.Invoke();
            return true;
        }

        return false;
    }
}

public sealed class ItemRow : Row
{
    public ItemRow(string primary, string secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public string Primary { get; }

    public string Secondary { get; }

    public bool Marked { get; init; }

    public bool ShowEdit { get; init; }

    public bool ShowRemove { get; init; }

    public Action? Open { get; set; }

    public Action? EditAction { get; set; }

    public Action? RemoveAction { get; set; }

    public override float Height(float width) => RowMetrics.ItemHeight;

    public override RowPart HitPart(float x, float y, float width)
    {
        var removeLeft = width - RowMetrics.Padding - RowMetrics.IconButton;
        if (ShowRemove && x >= removeLeft && x <= width - RowMetrics.Padding)
        {
            return RowPart.Remove;
        }

        var right = ShowRemove ? removeLeft - RowMetrics.IconGap : width - RowMetrics.Padding;
        var editLeft = right - RowMetrics.IconButton;
        if (ShowEdit && x >= editLeft && x <= right)
        {
            return RowPart.Edit;
        }

        return RowPart.Body;
    }

    public override bool Click(RowPart part, float fraction)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (part)
        {
            case RowPart.Edit:
                EditAction?.Invoke();
                return true;
            case RowPart.Remove:
                RemoveAction?.Invoke();
                return true;
            default:
                Open?.Invoke();
                return true;
        }
    }

    public override bool Key(Keys key)
    {
        if (!Enabled)
        {
            return false;
        }

        switch (key)
        {
            case Keys.Enter:
            case Keys.Space:
                Open?.Invoke();
                return true;
            case Keys.F2 when ShowEdit:
                EditAction?.Invoke();
                return true;
            case Keys.Delete when ShowRemove:
                RemoveAction?.Invoke();
                return true;
        }

        return false;
    }
}

public sealed class HistoryRow : Row
{
    public HistoryRow(HistoryEntry entry)
    {
        Entry = entry;
    }

    public HistoryEntry Entry { get; }

    public override bool Focusable => false;

    public override float Height(float width) => RowMetrics.HistoryHeight;
}

public enum LineLevel
{
    Normal,
    Alert,
    Error
}

public sealed class LineRow : Row
{
    public LineRow(string text, LineLevel level)
    {
        Text = text;
        Level = level;
    }

    public string Text { get; }

    public LineLevel Level { get; }

    public override bool Focusable => false;

    public static LineLevel LevelOf(string text)
    {
        var c = StringComparison.CurrentCultureIgnoreCase;
        if (text.Contains("ошибка", c) || text.Contains("Не удалось", c))
        {
            return LineLevel.Error;
        }

        return text.Contains("IP изменился", c) || text.Contains("Утечка", c) || text.Contains("VPN отключился", c) || text.Contains("тревог", c) || text.Contains("Защита сработала", c)
            ? LineLevel.Alert
            : LineLevel.Normal;
    }

    public override float Height(float width)
    {
        var lines = Math.Clamp(TextMetrics.Lines(Text, width - 2 * RowMetrics.Padding, RowMetrics.NoteFont), 1, RowMetrics.LineMaxLines);
        return lines * RowMetrics.NoteLine + 8f;
    }
}

public sealed class TextRow : Row
{
    public TextRow(string placeholder, string initial, int maxLength, Func<char, bool>? filter = null)
    {
        Placeholder = placeholder;
        Editor = new TextEditor(initial, maxLength, filter);
    }

    public string Placeholder { get; }

    public TextEditor Editor { get; }

    public override float Height(float width) => RowMetrics.TextHeight;

    public override bool Click(RowPart part, float fraction) => Enabled;
}
