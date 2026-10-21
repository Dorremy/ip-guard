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
    private readonly System.Windows.Forms.Timer timer = new() { Interval = FastInterval };
    private readonly Stopwatch clock = new();
    private readonly Tween open = new();
    private readonly ParticleField field = new(ParticleCount, NebulaRenderer.LogicalWidth, NebulaRenderer.LogicalHeight, 17);
    private readonly NebulaRenderer renderer = new();
    private readonly IntroDirector director;
    private PanelState state = PanelState.Initial;
    private Bitmap? buffer;
    private PanelPlacement? placement;
    private double lastTick;
    private float scale = 1f;
    private bool closing;

    public NebulaPanel(Action<string> run, Func<bool>? animationsEnabled = null)
    {
        this.run = run;
        this.animationsEnabled = animationsEnabled ?? (() => SystemInformation.UIEffectsEnabled);
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
    }

    public MenuModel Menu { get; }

    public IntroDirector Director => director;

    public IntroKind LastIntro { get; private set; } = IntroKind.None;

    public DateTime LastClosedUtc { get; private set; } = DateTime.MinValue;

    public bool Opened => Visible && !closing;

    public bool Animating => timer.Enabled;

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
        if (Menu.Move(logical))
        {
            Cursor = Menu.Hover >= 0 ? Cursors.Hand : Cursors.Default;
        }
    }

    public void PointerPressed(PointF logical) => Menu.Press(logical);

    public void PointerReleased(PointF logical) => Choose(Menu.Release(logical));

    public void PointerLeft() => Menu.Leave();

    public bool KeyPressed(Keys key)
    {
        if (key == Keys.Escape)
        {
            BeginClose();
            return true;
        }

        var handled = key is Keys.Down or Keys.Up or Keys.Tab or Keys.Home or Keys.End or Keys.Enter or Keys.Space;
        Choose(Menu.Key(key));
        return handled;
    }

    private void Choose(string? id)
    {
        if (id is null)
        {
            return;
        }

        if (id == "check")
        {
            run(id);
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

        var settled = director.Scene == Scene.Idle && open.Finished && !closing;
        var interval = settled ? IdleInterval : FastInterval;
        if (timer.Interval != interval)
        {
            timer.Interval = interval;
        }

        if (buffer is not null)
        {
            renderer.Render(buffer, director, Menu);
            Invalidate();
        }
    }

    private void HideNow()
    {
        timer.Stop();
        clock.Stop();
        if (closing || Visible)
        {
            LastClosedUtc = DateTime.UtcNow;
        }

        closing = false;
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Tab or Keys.Down or Keys.Up or Keys.Home or Keys.End or Keys.Escape or Keys.Enter or Keys.Space)
        {
            return KeyPressed(keyData);
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
