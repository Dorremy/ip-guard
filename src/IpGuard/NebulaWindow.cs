using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class NebulaWindow : Form
{
    public const int LogicalWidth = 700;
    public const int LogicalHeight = 520;
    public static readonly Rectangle FrameArea = new(14, 14, 672, 492);

    private const int WsExToolWindow = 0x80;
    private const int CsDropShadow = 0x20000;
    private const int DwmCornerPreference = 33;
    private const int DwmDarkMode = 20;
    private const int RoundCorners = 2;
    private const double OpenSeconds = 0.32;
    private const double CloseSeconds = 0.2;
    private const int FastInterval = 16;
    private const int IdleInterval = 33;

    private readonly System.Windows.Forms.Timer timer = new() { Interval = FastInterval };
    private readonly Stopwatch clock = new();
    private readonly Tween open = new();
    private readonly AmbientScene scene = new(LogicalWidth, LogicalHeight, 41);
    private readonly WindowRenderer renderer = new(new Size(LogicalWidth, LogicalHeight));
    private readonly PageHost pages = new(new PageFrame(FrameArea)) { BackClosesRoot = false, ShowClose = true };
    private readonly PageInput input;
    private Bitmap? buffer;
    private double lastTick;
    private float scale = 1f;
    private bool closing;
    private bool caretOn = true;

    public NebulaWindow(Page root)
    {
        input = new PageInput(pages, () => clock.Elapsed.TotalSeconds, 0f);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        BackColor = Color.Black;
        Text = root.Title;
        AccessibleName = root.Title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true);
        timer.Tick += (_, _) => Tick();
        pages.CloseRequested += BeginClose;
        pages.Push(root);
    }

    public PageHost Pages => pages;

    public AmbientScene Scene => scene;

    public bool Animating => timer.Enabled;

    public bool Closing => closing;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void SetTone(Tone tone) => scene.Tone = tone;

    public void Open(Rectangle panelBounds, Rectangle work)
    {
        float dpi;
        using (var g = CreateGraphics())
        {
            dpi = g.DpiX;
        }

        var logical = new Size(LogicalWidth, LogicalHeight);
        scale = PanelPlacer.ScaleFor(dpi, work, logical);
        var size = PanelPlacer.Physical(logical, scale);
        ClientSize = size;
        Location = WindowPlacer.Place(size, panelBounds, work, (int)(48 * scale));
        if (buffer is null || buffer.Size != size)
        {
            buffer?.Dispose();
            buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        }

        ApplyShape();
        closing = false;
        Opacity = 0;
        open.Start(0, 1, OpenSeconds, Easing.OutCubic);
        Show();
        Activate();
        clock.Restart();
        lastTick = 0;
        timer.Interval = FastInterval;
        timer.Start();
        Tick();
    }

    public void BeginClose()
    {
        if (closing || !Visible)
        {
            return;
        }

        closing = true;
        open.StartFromCurrent(0, CloseSeconds, Easing.InCubic);
    }

    public void PointerMoved(PointF logical) => Cursor = CursorFor(input.Moved(logical));

    public void PointerPressed(PointF logical) => input.Pressed(logical);

    public void PointerReleased(PointF logical) => input.Released(logical);

    public void PointerLeft() => input.Left();

    public void WheelScrolled(float notches) => input.Wheel(notches);

    public void CharTyped(char c) => input.Char(c);

    public bool KeyPressed(Keys keyData) => input.Key(keyData);

    private static Cursor CursorFor(CursorKind kind) => kind switch
    {
        CursorKind.Hand => Cursors.Hand,
        CursorKind.IBeam => Cursors.IBeam,
        _ => Cursors.Default
    };

    private void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - lastTick, 0, 0.25);
        lastTick = now;
        scene.Step(dt);
        open.Update(dt);
        pages.Step(dt);
        pages.Tick(now);
        input.Tick(now);
        caretOn = (long)(now * 2) % 2 == 0;
        Opacity = Math.Clamp(open.Value, 0, 1);
        if (closing && open.Finished)
        {
            timer.Stop();
            clock.Stop();
            input.Reset();
            Close();
            return;
        }

        var interval = open.Finished && !closing && pages.ContentAlpha >= 1f ? IdleInterval : FastInterval;
        if (timer.Interval != interval)
        {
            timer.Interval = interval;
        }

        if (buffer is not null)
        {
            renderer.Render(buffer, scene, pages, caretOn, (float)open.Value);
            Invalidate();
        }
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
