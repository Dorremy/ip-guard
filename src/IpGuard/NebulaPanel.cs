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
    private const int IdleInterval = 20;
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
    private readonly PageInput input;
    private PanelState state = PanelState.Initial;
    private Bitmap? buffer;
    private PanelPlacement? placement;
    private double lastTick;
    private float scale = 1f;
    private bool closing;
    private bool caretOn = true;
    private bool alertMode;
    private bool openedByAlert;
    private int urgent;
    private bool activateOnShow = true;
    private readonly System.Windows.Forms.Timer focusCheck = new() { Interval = 250 };

    public NebulaPanel(Action<string> run, Func<bool>? animationsEnabled = null, PanelServices? services = null)
    {
        this.run = run;
        this.animationsEnabled = animationsEnabled ?? (() => SystemInformation.UIEffectsEnabled);
        this.services = services ?? new PanelServices();
        input = new PageInput(pages, () => clock.Elapsed.TotalSeconds, 0.45f);
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
        focusCheck.Tick += (_, _) => FocusChecked();
        Deactivate += (_, _) => NotifyFocusLost();
        pages.Navigated += () => director.Surge(pages.IsOpen ? PageGeometry.Area : Menu.Area(1f));
    }

    public MenuModel Menu { get; }

    public PageHost Pages => pages;

    public IntroDirector Director => director;

    public IntroKind LastIntro { get; private set; } = IntroKind.None;

    public DateTime LastClosedUtc { get; private set; } = DateTime.MinValue;

    public bool Opened => Visible && !closing;

    public bool AlertActive => alertMode;

    public bool ProtectActive => pages.Stack.Any(page => page is ProtectPage { Finished: false });

    public event Action? AlertDismissed;

    protected override bool ShowWithoutActivation => !activateOnShow;

    public Func<bool> ForegroundIsOurs { get; set; } = OwnsForeground;

    public bool Animating => timer.Enabled;

    private bool PageOpen => pages.IsOpen;

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void Apply(PanelState next)
    {
        state = next;
        director.SetState(next);
        AccessibleName = next.Label;
    }

    public void SetChecking(bool checking) => director.Checking = checking;

    public void Open(Point cursor, bool activate = true)
    {
        activateOnShow = activate;
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
        input.Reset();
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

        if (activate)
        {
            Activate();
        }

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
        if (input.Active)
        {
            Cursor = CursorFor(input.Moved(logical));
            return;
        }

        if (Menu.Move(logical))
        {
            Cursor = Menu.Hover >= 0 ? Cursors.Hand : Cursors.Default;
        }
    }

    public void PointerPressed(PointF logical)
    {
        if (input.Active)
        {
            input.Pressed(logical);
            return;
        }

        Menu.Press(logical);
    }

    public void PointerReleased(PointF logical)
    {
        if (input.Active)
        {
            input.Released(logical);
            return;
        }

        Choose(Menu.Release(logical));
    }

    public void PointerLeft()
    {
        Menu.Leave();
        input.Left();
    }

    public void WheelScrolled(float notches)
    {
        if (input.Active)
        {
            input.Wheel(notches);
        }
    }

    public void CharTyped(char c)
    {
        if (PageOpen)
        {
            input.Char(c);
        }
    }

    public bool KeyPressed(Keys keyData)
    {
        if (PageOpen)
        {
            return input.Key(keyData);
        }

        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape)
        {
            BeginClose();
            return true;
        }

        var handled = key is Keys.Down or Keys.Up or Keys.Tab or Keys.Home or Keys.End or Keys.Enter or Keys.Space;
        Choose(Menu.Key(key));
        return handled;
    }

    private static Cursor CursorFor(CursorKind kind) => kind switch
    {
        CursorKind.Hand => Cursors.Hand,
        CursorKind.IBeam => Cursors.IBeam,
        _ => Cursors.Default
    };

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
            case "references":
            case "history":
            case "journal":
                ShowPage(id);
                return;
        }

        HideNow();
        BeginInvoke(() => run(id));
    }

    public void NotifyFocusLost()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        focusCheck.Stop();
        focusCheck.Start();
    }

    private void FocusChecked()
    {
        focusCheck.Stop();
        if (IsDisposed || !Visible || closing)
        {
            return;
        }

        if (!alertMode && !ForegroundIsOurs())
        {
            BeginClose();
        }
    }

    public void ShowPage(string id)
    {
        Page? page = id switch
        {
            "settings" => new SettingsPage(services),
            "references" => new ReferencesPage(services),
            "history" => new HistoryPage(services),
            "journal" => new JournalPage(services),
            "tray" => new TrayPage(services, () => state),
            _ => null
        };
        if (page is not null)
        {
            ShowPage(page);
        }
    }

    public void ShowPage(Page page)
    {
        if (pages.IsOpen && !alertMode)
        {
            pages.Clear();
        }

        pages.Push(page);
    }

    public void ShowAlert(Verdict verdict, bool canAccept, Action? accept, Point near)
    {
        if (alertMode && pages.Current is AlertPage existing)
        {
            existing.Show(verdict.Title, verdict.Message, canAccept, () => accept?.Invoke());
            pages.Touch();
            return;
        }

        if (pages.Current is ProtectPage)
        {
            return;
        }

        BeginUrgent(near);
        pages.Push(new AlertPage(verdict.Title, verdict.Message, canAccept, () => accept?.Invoke(), DismissAlert));
    }

    public void ShowProtect(string reason, IReadOnlyList<string> programs, int seconds, Action onClose, Action onCancel, Point near)
    {
        BeginUrgent(near);
        pages.Push(new ProtectPage(reason, programs, seconds, onClose, onCancel, () => Urgent(-1)));
    }

    public void ResolveAlert(string text)
    {
        if (alertMode && pages.Current is AlertPage { Resolved: false } page)
        {
            page.Resolve(text);
            pages.Touch();
        }
    }

    private void BeginUrgent(Point near)
    {
        if (!Visible || closing)
        {
            openedByAlert = true;
            alertMode = true;
            urgent = 0;
            Open(near, false);
        }
        else if (!alertMode)
        {
            openedByAlert = false;
        }

        alertMode = true;
        urgent++;
    }

    private void Urgent(int delta)
    {
        urgent = Math.Max(0, urgent + delta);
        if (urgent == 0 && alertMode)
        {
            alertMode = false;
            if (openedByAlert)
            {
                openedByAlert = false;
                BeginClose();
            }
        }
    }

    private void DismissAlert()
    {
        AlertDismissed?.Invoke();
        Urgent(-1);
    }

    private void TearDownUrgent()
    {
        var hadAlert = false;
        foreach (var page in pages.Stack.ToList())
        {
            switch (page)
            {
                case ProtectPage protect:
                    protect.Abort();
                    break;
                case AlertPage:
                    hadAlert = true;
                    break;
            }
        }

        alertMode = false;
        openedByAlert = false;
        urgent = 0;
        if (hadAlert)
        {
            AlertDismissed?.Invoke();
        }
    }

    private static bool OwnsForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(foreground, out var pid);
        return pid == Environment.ProcessId;
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
        pages.Tick(now);
        input.Tick(now);
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

    private void HideNow()
    {
        timer.Stop();
        clock.Stop();
        input.Reset();
        focusCheck.Stop();
        if (closing || Visible)
        {
            LastClosedUtc = DateTime.UtcNow;
        }

        closing = false;
        TearDownUrgent();
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
            focusCheck.Dispose();
            renderer.Dispose();
            buffer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
