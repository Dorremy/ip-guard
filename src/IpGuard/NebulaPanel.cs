using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class NebulaPanel : Form
{
    private const int WsExToolWindow = 0x80;
    private const int CsDropShadow = 0x20000;
    private const int DwmCornerPreference = 33;
    private const int DwmDarkMode = 20;
    private const int RoundCorners = 2;
    private const double OpenSeconds = 0.32;
    private const double CloseSeconds = 0.16;
    private const double ShortIntroWindow = 120;
    private const int FastInterval = 15;
    private const int IdleInterval = 33;
    private const int ParticleCount = 2800;

    private readonly Action<string> run;
    private readonly Func<bool> animationsEnabled;
    private readonly PanelServices services;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = FastInterval };
    private readonly Stopwatch clock = new();
    private readonly Tween open = new();
    private readonly ParticleField field = new(ParticleCount, NebulaRenderer.LogicalWidth, NebulaRenderer.LogicalHeight, 17);
    private readonly NebulaRenderer renderer = new();
    private readonly IntroDirector director;
    private readonly PageHost pages = new();
    private readonly HoldRepeater hold = new();
    private PanelState state = PanelState.Initial;
    private Bitmap? buffer;
    private PanelPlacement? placement;
    private double lastTick;
    private float scale = 1f;
    private bool closing;
    private bool textDrag;
    private StepperRow? holdRow;
    private RowPart holdPart;
    private bool caretOn = true;

    public NebulaPanel(Action<string> run, Func<bool>? animationsEnabled = null, PanelServices? services = null)
    {
        this.run = run;
        this.animationsEnabled = animationsEnabled ?? (() => SystemInformation.UIEffectsEnabled);
        this.services = services ?? new PanelServices();
        director = new IntroDirector(field, NebulaTargets.Intro(), NebulaTargets.Status, IntroKind.None);
        Menu = MenuModel.Default();
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        Text = "IpGuard";
        AccessibleName = "IpGuard";
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true);
        timer.Tick += (_, _) => Tick();
        Deactivate += (_, _) => BeginClose();
        pages.Navigated += () => director.Surge(pages.IsOpen ? PageGeometry.Area : Menu.Area(1f));
    }

    public MenuModel Menu { get; }

    public PageHost Pages => pages;

    public IntroDirector Director => director;

    public IntroKind LastIntro { get; private set; } = IntroKind.None;

    public DateTime LastClosedUtc { get; private set; } = DateTime.MinValue;

    public bool Opened => Visible && !closing;

    public bool Animating => timer.Enabled;

    private bool PageOpen => pages.IsOpen;

    private bool PageActive => pages.IsOpen && pages.Progress > 0.45f;

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

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void Apply(PanelState next)
    {
        state = next;
        director.SetState(next);
        AccessibleName = next.Label;
    }

    public void SetChecking(bool checking) => director.Checking = checking;

    public void Open(Point cursor)
    {
        var screen = Screen.FromPoint(cursor);
        float dpi;
        using (var g = CreateGraphics())
        {
            dpi = g.DpiX;
        }

        var logical = new Size(NebulaRenderer.LogicalWidth, NebulaRenderer.LogicalHeight);
        scale = PanelPlacer.ScaleFor(dpi, screen.WorkingArea, logical);
        var size = PanelPlacer.Physical(logical, scale);
        placement = PanelPlacer.Place(size, screen.Bounds, screen.WorkingArea, cursor);
        var wasVisible = Visible;
        closing = false;
        pages.Clear();
        ClientSize = size;
        if (buffer is null || buffer.Size != size)
        {
            buffer?.Dispose();
            buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        }

        ApplyShape();
        LastIntro = ChooseIntro();
        director.Restart(LastIntro);
        director.SetState(state);
        Menu.Leave();
        if (wasVisible)
        {
            open.StartFromCurrent(1, OpenSeconds, Easing.OutCubic);
        }
        else
        {
            Location = placement.SlideFrom;
            Opacity = 0;
            open.Start(0, 1, OpenSeconds, Easing.OutCubic);
            Show();
        }

        Activate();
        clock.Restart();
        lastTick = 0;
        timer.Interval = FastInterval;
        timer.Start();
        Tick();
    }

    public void BeginClose()
    {
        if (!Visible || closing)
        {
            return;
        }

        closing = true;
        LastClosedUtc = DateTime.UtcNow;
        open.StartFromCurrent(0, CloseSeconds, Easing.InCubic);
    }

    public void PointerMoved(PointF logical)
    {
        if (PageActive)
        {
            MovePage(logical);
            return;
        }

        if (Menu.Move(logical))
        {
            Cursor = Menu.Hover >= 0 ? Cursors.Hand : Cursors.Default;
        }
    }

    public void PointerPressed(PointF logical)
    {
        if (PageActive)
        {
            PressPage(logical);
            return;
        }

        Menu.Press(logical);
    }

    public void PointerReleased(PointF logical)
    {
        if (PageActive)
        {
            ReleasePage(logical);
            return;
        }

        Choose(Menu.Release(logical));
    }

    public void PointerLeft()
    {
        Menu.Leave();
        if (pages.Current is { } page)
        {
            page.Rows.Leave();
            pages.FooterHover = -1;
            pages.BackHover = false;
        }
    }

    public void WheelScrolled(float notches)
    {
        if (PageActive && pages.Current is { } page)
        {
            page.Rows.Wheel(notches);
        }
    }

    public void CharTyped(char c)
    {
        if (!PageOpen || char.IsControl(c) || FocusedText is not { } text || pages.Current is not { } page)
        {
            return;
        }

        text.Editor.Insert(c.ToString());
        page.OnInput();
    }

    public bool KeyPressed(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var ctrl = (keyData & Keys.Control) != 0;
        var shift = (keyData & Keys.Shift) != 0;
        if (PageOpen)
        {
            return PageKey(key, ctrl, shift);
        }

        if (key == Keys.Escape)
        {
            BeginClose();
            return true;
        }

        var handled = key is Keys.Down or Keys.Up or Keys.Tab or Keys.Home or Keys.End or Keys.Enter or Keys.Space;
        Choose(Menu.Key(key));
        return handled;
    }

    private bool PageKey(Keys key, bool ctrl, bool shift)
    {
        var page = pages.Current!;
        var rows = page.Rows;
        var text = FocusedText;
        if (key == Keys.Escape)
        {
            pages.Pop();
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

        if (text is not null)
        {
            return TextKey(text, page, key, ctrl, shift);
        }

        return rows.Key(key);
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

    private void MovePage(PointF logical)
    {
        var page = pages.Current!;
        var viewport = PageGeometry.Viewport(page);
        var inside = viewport.Contains((int)MathF.Floor(logical.X), (int)MathF.Floor(logical.Y));
        pages.BackHover = PageGeometry.IsBack(logical);
        pages.FooterHover = page.Footer.Count > 0 && PageGeometry.FooterAt(page, logical) is var f and >= 0 && page.Footer[f].IsEnabled ? f : -1;
        var local = PageGeometry.ToRows(page, logical);
        if (textDrag && FocusedText is { } text)
        {
            text.Editor.SetCaret(CaretAt(text, logical), select: true);
            page.OnInput();
        }

        page.Rows.Drag(local);
        page.Rows.Move(inside ? local : new PointF(-1, -1));
        var rows = page.Rows;
        Cursor = pages.BackHover || pages.FooterHover >= 0 || (rows.Hover >= 0 && rows.Rows[rows.Hover] is not TextRow)
            ? Cursors.Hand
            : rows.Hover >= 0 ? Cursors.IBeam : Cursors.Default;
    }

    private void PressPage(PointF logical)
    {
        var page = pages.Current!;
        if (PageGeometry.IsBack(logical))
        {
            pages.BackPressed = true;
            return;
        }

        var footer = PageGeometry.FooterAt(page, logical);
        if (footer >= 0)
        {
            pages.FooterPressed = page.Footer[footer].IsEnabled ? footer : -1;
            return;
        }

        if (!PageGeometry.Viewport(page).Contains((int)MathF.Floor(logical.X), (int)MathF.Floor(logical.Y)))
        {
            return;
        }

        var rows = page.Rows;
        rows.Press(PageGeometry.ToRows(page, logical));
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
            hold.Begin(clock.Elapsed.TotalSeconds);
        }
    }

    private void ReleasePage(PointF logical)
    {
        var page = pages.Current!;
        textDrag = false;
        if (pages.BackPressed)
        {
            var on = PageGeometry.IsBack(logical);
            pages.BackPressed = false;
            if (on)
            {
                pages.Pop();
            }

            return;
        }

        if (pages.FooterPressed >= 0)
        {
            var index = pages.FooterPressed;
            pages.FooterPressed = -1;
            if (PageGeometry.FooterAt(page, logical) == index)
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

        page.Rows.Release(PageGeometry.ToRows(page, logical));
    }

    private static int CaretAt(TextRow text, PointF logical)
    {
        var left = PageGeometry.Area.X + 10 + 9;
        return PageDrawing.CaretIndexAt(text.Editor.Text, logical.X - left);
    }

    private void Choose(string? id)
    {
        if (id is null)
        {
            return;
        }

        switch (id)
        {
            case "check":
                run(id);
                return;
            case "settings":
                pages.Push(new SettingsPage(services));
                return;
            case "references":
                pages.Push(new ReferencesPage(services));
                return;
        }

        HideNow();
        BeginInvoke(() => run(id));
    }

    private IntroKind ChooseIntro()
    {
        if (!animationsEnabled())
        {
            return IntroKind.None;
        }

        return (DateTime.UtcNow - LastClosedUtc).TotalSeconds < ShortIntroWindow ? IntroKind.Short : IntroKind.Full;
    }

    private void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - lastTick, 0, 0.25);
        lastTick = now;
        director.Step(dt);
        open.Update(dt);
        pages.Step(dt);
        RepeatHold(now);
        caretOn = (long)(now * 2) % 2 == 0;
        if (placement is { } p)
        {
            var t = open.Value;
            Location = new Point(
                (int)Math.Round(p.SlideFrom.X + (p.Location.X - p.SlideFrom.X) * t),
                (int)Math.Round(p.SlideFrom.Y + (p.Location.Y - p.SlideFrom.Y) * t));
            Opacity = Math.Clamp(t, 0, 1);
        }

        if (closing && open.Finished)
        {
            HideNow();
            return;
        }

        var pageMoving = pages.Progress is > 0f and < 1f || pages.ContentAlpha < 1f;
        var settled = director.Scene == Scene.Idle && open.Finished && !closing && !pageMoving;
        var interval = settled ? IdleInterval : FastInterval;
        if (timer.Interval != interval)
        {
            timer.Interval = interval;
        }

        if (buffer is not null)
        {
            renderer.Render(buffer, director, Menu, pages, caretOn);
            Invalidate();
        }
    }

    private void RepeatHold(double now)
    {
        if (!hold.Active || holdRow is null || pages.Current is not { } page)
        {
            return;
        }

        if (page.Rows.Pressed < 0 || page.Rows.HoverPart != holdPart)
        {
            return;
        }

        var pulses = hold.Pulses(now);
        var multiplier = hold.Multiplier(now);
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

    private void HideNow()
    {
        timer.Stop();
        clock.Stop();
        hold.Cancel();
        holdRow = null;
        textDrag = false;
        if (closing || Visible)
        {
            LastClosedUtc = DateTime.UtcNow;
        }

        closing = false;
        pages.Clear();
        Hide();
    }

    private void ApplyShape()
    {
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            Region = null;
            return;
        }

        using var path = new GraphicsPath();
        var d = 16 * scale;
        var r = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var corner = RoundCorners;
        var dark = 1;
        DwmSetWindowAttribute(Handle, DwmCornerPreference, ref corner, sizeof(int));
        DwmSetWindowAttribute(Handle, DwmDarkMode, ref dark, sizeof(int));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (buffer is not null)
        {
            e.Graphics.DrawImageUnscaled(buffer, 0, 0);
        }
    }

    private PointF Logical(Point p) => new(p.X / scale, p.Y / scale);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMoved(Logical(e.Location));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            PointerPressed(Logical(e.Location));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            PointerReleased(Logical(e.Location));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        PointerLeft();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        WheelScrolled(e.Delta / 120f);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!char.IsControl(e.KeyChar) && FocusedText is not null)
        {
            CharTyped(e.KeyChar);
            e.Handled = true;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (KeyPressed(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            renderer.Dispose();
            buffer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
