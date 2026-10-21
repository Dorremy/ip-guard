using System.Runtime.InteropServices;

namespace IpGuard;

public enum CursorKind
{
    Default,
    Hand,
    IBeam
}

public sealed class PageInput
{
    private readonly PageHost pages;
    private readonly Func<double> now;
    private readonly float minProgress;
    private readonly HoldRepeater hold = new();
    private bool textDrag;
    private StepperRow? holdRow;
    private RowPart holdPart;

    public PageInput(PageHost pages, Func<double> now, float minProgress)
    {
        this.pages = pages;
        this.now = now;
        this.minProgress = minProgress;
    }

    public bool Active => pages.IsOpen && pages.Progress > minProgress;

    private PageFrame Frame => pages.Frame;

    private TextRow? FocusedText
    {
        get
        {
            if (pages.Current is not { } page)
            {
                return null;
            }

            var rows = page.Rows;
            return rows.Focus >= 0 && rows.Focus < rows.Rows.Count ? rows.Rows[rows.Focus] as TextRow : null;
        }
    }

    public void Reset()
    {
        hold.Cancel();
        holdRow = null;
        textDrag = false;
    }

    public CursorKind Moved(PointF logical)
    {
        if (pages.Current is not { } page)
        {
            return CursorKind.Default;
        }

        var viewport = Frame.Viewport(page);
        var inside = viewport.Contains((int)MathF.Floor(logical.X), (int)MathF.Floor(logical.Y));
        pages.BackHover = pages.CanGoBack && Frame.IsBack(logical);
        pages.CloseHover = pages.ShowClose && Frame.IsClose(logical);
        var footer = page.Footer.Count > 0 ? Frame.FooterAt(page, logical) : -1;
        pages.FooterHover = footer >= 0 && page.Footer[footer].IsEnabled ? footer : -1;
        var local = Frame.ToRows(page, logical);
        if (textDrag && FocusedText is { } text)
        {
            text.Editor.SetCaret(CaretAt(text, logical), select: true);
            page.OnInput();
        }

        page.Rows.Drag(local);
        page.Rows.Move(inside ? local : new PointF(-1, -1));
        var rows = page.Rows;
        if (pages.BackHover || pages.CloseHover || pages.FooterHover >= 0 || (rows.Hover >= 0 && rows.Rows[rows.Hover] is not TextRow))
        {
            return CursorKind.Hand;
        }

        return rows.Hover >= 0 ? CursorKind.IBeam : CursorKind.Default;
    }

    public void Pressed(PointF logical)
    {
        if (pages.Current is not { } page)
        {
            return;
        }

        if (pages.CanGoBack && Frame.IsBack(logical))
        {
            pages.BackPressed = true;
            return;
        }

        if (pages.ShowClose && Frame.IsClose(logical))
        {
            pages.ClosePressed = true;
            return;
        }

        var footer = page.Footer.Count > 0 ? Frame.FooterAt(page, logical) : -1;
        if (footer >= 0)
        {
            pages.FooterPressed = page.Footer[footer].IsEnabled ? footer : -1;
            return;
        }

        if (!Frame.Viewport(page).Contains((int)MathF.Floor(logical.X), (int)MathF.Floor(logical.Y)))
        {
            return;
        }

        var rows = page.Rows;
        rows.Press(Frame.ToRows(page, logical));
        if (rows.Pressed >= 0 && rows.Rows[rows.Pressed] is TextRow text)
        {
            text.Editor.SetCaret(CaretAt(text, logical));
            textDrag = true;
            page.OnInput();
        }
        else if (rows.Pressed >= 0 && rows.Rows[rows.Pressed] is StepperRow stepper && rows.PressedPart is RowPart.Minus or RowPart.Plus)
        {
            holdRow = stepper;
            holdPart = rows.PressedPart;
            hold.Begin(now());
        }
    }

    public void Released(PointF logical)
    {
        if (pages.Current is not { } page)
        {
            return;
        }

        textDrag = false;
        if (pages.BackPressed)
        {
            var on = Frame.IsBack(logical);
            pages.BackPressed = false;
            if (on)
            {
                pages.Pop();
            }

            return;
        }

        if (pages.ClosePressed)
        {
            var on = Frame.IsClose(logical);
            pages.ClosePressed = false;
            if (on)
            {
                pages.RequestClose();
            }

            return;
        }

        if (pages.FooterPressed >= 0)
        {
            var index = pages.FooterPressed;
            pages.FooterPressed = -1;
            if (Frame.FooterAt(page, logical) == index)
            {
                pages.ClickFooter(index);
            }

            return;
        }

        var repeated = hold.Active && hold.Repeated;
        hold.Cancel();
        holdRow = null;
        if (repeated)
        {
            page.Rows.CancelPress();
        }

        page.Rows.Release(Frame.ToRows(page, logical));
    }

    public void Left()
    {
        if (pages.Current is { } page)
        {
            page.Rows.Leave();
        }

        pages.FooterHover = -1;
        pages.BackHover = false;
        pages.CloseHover = false;
    }

    public void Wheel(float notches)
    {
        if (pages.Current is { } page)
        {
            page.Rows.Wheel(notches);
        }
    }

    public void Char(char c)
    {
        if (char.IsControl(c) || FocusedText is not { } text || pages.Current is not { } page)
        {
            return;
        }

        text.Editor.Insert(c.ToString());
        page.OnInput();
    }

    public bool Key(Keys keyData)
    {
        if (pages.Current is not { } page)
        {
            return false;
        }

        var key = keyData & Keys.KeyCode;
        var ctrl = (keyData & Keys.Control) != 0;
        var shift = (keyData & Keys.Shift) != 0;
        var rows = page.Rows;
        if (key == Keys.Escape)
        {
            if (pages.CanGoBack)
            {
                pages.Pop();
            }
            else
            {
                pages.RequestClose();
            }

            return true;
        }

        if (ctrl && key == Keys.Enter)
        {
            pages.Accept();
            return true;
        }

        switch (key)
        {
            case Keys.Tab:
                rows.MoveFocus(shift ? -1 : 1);
                return true;
            case Keys.Down:
                rows.MoveFocus(1);
                return true;
            case Keys.Up:
                rows.MoveFocus(-1);
                return true;
            case Keys.PageDown:
                rows.ScrollBy(rows.ViewportHeight * 0.9f);
                return true;
            case Keys.PageUp:
                rows.ScrollBy(-rows.ViewportHeight * 0.9f);
                return true;
        }

        return FocusedText is { } text ? TextKey(text, page, key, ctrl, shift) : rows.Key(key);
    }

    public void Tick(double time)
    {
        if (!hold.Active || holdRow is null || pages.Current is not { } page)
        {
            return;
        }

        if (page.Rows.Pressed < 0 || page.Rows.HoverPart != holdPart)
        {
            return;
        }

        var pulses = hold.Pulses(time);
        var multiplier = hold.Multiplier(time);
        for (var i = 0; i < pulses; i++)
        {
            if (holdPart == RowPart.Plus)
            {
                holdRow.Plus(multiplier);
            }
            else
            {
                holdRow.Minus(multiplier);
            }
        }
    }

    private bool TextKey(TextRow text, Page page, Keys key, bool ctrl, bool shift)
    {
        var editor = text.Editor;
        var edited = true;
        switch (key)
        {
            case Keys.Left:
                editor.Move(-1, shift);
                edited = false;
                break;
            case Keys.Right:
                editor.Move(1, shift);
                edited = false;
                break;
            case Keys.Home:
                editor.Home(shift);
                edited = false;
                break;
            case Keys.End:
                editor.End(shift);
                edited = false;
                break;
            case Keys.Back:
                editor.Backspace();
                break;
            case Keys.Delete:
                editor.Delete();
                break;
            case Keys.Enter:
                pages.Accept();
                return true;
            case Keys.A when ctrl:
                editor.SelectAll();
                edited = false;
                break;
            case Keys.C when ctrl:
                CopyText(editor.SelectedText);
                edited = false;
                break;
            case Keys.X when ctrl:
                CopyText(editor.CutSelection());
                break;
            case Keys.V when ctrl:
                editor.Insert(PasteText());
                break;
            default:
                return false;
        }

        if (edited)
        {
            page.OnInput();
        }

        return true;
    }

    private int CaretAt(TextRow text, PointF logical)
    {
        var left = Frame.Area.X + 10 + 9;
        return PageDrawing.CaretIndexAt(text.Editor.Text, logical.X - left);
    }

    private static void CopyText(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
        }
        catch (ExternalException)
        {
        }
    }

    private static string PasteText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (ExternalException)
        {
            return "";
        }
    }
}
