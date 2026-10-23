using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class ToastWindow : Form
{
    public const int LogicalWidth = 360;
    public const int LogicalHeight = 104;

    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int CsDropShadow = 0x20000;
    private const int DwmCornerPreference = 33;
    private const int DwmDarkMode = 20;
    private const int RoundCorners = 2;

    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly Stopwatch clock = new();
    private readonly ToastTimeline timeline = new();
    private readonly MiniSwarm swarm = new(260, LogicalWidth, LogicalHeight, 5);
    private Bitmap? buffer;
    private PanelPlacement? placement;
    private double last;
    private float scale = 1f;
    private float warm;
    private string title = "";
    private string message = "";
    private Tone tone = Tone.Calm;

    public ToastWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Text = "IpGuard";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true);
        timer.Tick += (_, _) => Advance((float)Elapsed());
    }

    public event Action? Clicked;

    public string ToastTitle => title;

    public string ToastMessage => message;

    public Tone ToastTone => tone;

    public ToastTimeline Timeline => timeline;

    public int ExtendedStyle => CreateParams.ExStyle;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void Present(string newTitle, string newMessage, Tone newTone, Rectangle bounds, Rectangle work)
    {
        (title, message) = ToastPolicy.Fit(newTitle, newMessage);
        tone = newTone;
        float dpi;
        using (var g = CreateGraphics())
        {
            dpi = g.DpiX;
        }

        var logical = new Size(LogicalWidth, LogicalHeight);
        scale = PanelPlacer.ScaleFor(dpi, work, logical);
        var size = PanelPlacer.Physical(logical, scale);
        ClientSize = size;
        placement = PanelPlacer.Place(size, bounds, work, new Point(work.Right, work.Bottom));
        if (buffer is null || buffer.Size != size)
        {
            buffer?.Dispose();
            buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        }

        ApplyShape();
        warm = tone == Tone.Alert ? 1f : 0f;
        timeline.Start(ToastPolicy.HoldFor(tone));
        if (!Visible)
        {
            Location = placement.SlideFrom;
            Opacity = 0;
            Show();
        }

        clock.Restart();
        last = 0;
        timer.Start();
        Advance(0.016f);
    }

    public Bitmap Snapshot()
    {
        var size = buffer?.Size ?? PanelPlacer.Physical(new Size(LogicalWidth, LogicalHeight), scale);
        var shot = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        Draw(shot);
        return shot;
    }

    public void Dismiss()
    {
        timeline.Dismiss();
    }

    public void Advance(float dt)
    {
        if (!Visible)
        {
            return;
        }

        timeline.Hovering = new Rectangle(Location, Size).Contains(Cursor.Position);
        timeline.Step(dt);
        swarm.Step(dt);
        if (timeline.Finished)
        {
            timer.Stop();
            clock.Stop();
            Hide();
            return;
        }

        if (placement is { } p)
        {
            var t = timeline.Appear;
            Location = new Point(
                (int)Math.Round(p.SlideFrom.X + (p.Location.X - p.SlideFrom.X) * t),
                (int)Math.Round(p.SlideFrom.Y + (p.Location.Y - p.SlideFrom.Y) * t));
        }

        Opacity = Math.Clamp(timeline.Alpha, 0, 1);
        if (buffer is not null)
        {
            Draw(buffer);
            Invalidate();
        }
    }

    private double Elapsed()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - last, 0, 0.25);
        last = now;
        return dt;
    }

    private void Draw(Bitmap target)
    {
        swarm.Render(target, (float)clock.Elapsed.TotalSeconds + 7f, warm, timeline.Appear);
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var w = target.Width;
        var h = target.Height;
        using (var shade = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(120, 0, 0, 0), Color.FromArgb(40, 0, 0, 0), 0f))
        {
            g.FillRectangle(shade, 0, 0, w, h);
        }

        var accent = NebulaRenderer.ToneColor(tone);
        using (var dot = new SolidBrush(Color.FromArgb(235, accent)))
        using (var glow = new SolidBrush(Color.FromArgb(60, accent)))
        {
            var cy = 26 * scale;
            g.FillEllipse(glow, 12 * scale, cy - 9 * scale, 18 * scale, 18 * scale);
            g.FillEllipse(dot, 16 * scale, cy - 5 * scale, 10 * scale, 10 * scale);
        }

        using var titleFont = new Font("Segoe UI Semibold", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textFont = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(240, 242, 248));
        using var dim = new SolidBrush(Color.FromArgb(205, 190, 198, 212));
        using var line = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        using var wrap = new StringFormat(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisWord };
        g.DrawString(title, titleFont, ink, new RectangleF(38 * scale, 14 * scale, w - 50 * scale, 24 * scale), line);
        g.DrawString(message, textFont, dim, new RectangleF(16 * scale, 46 * scale, w - 32 * scale, 50 * scale), wrap);
        using var edge = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(1f, scale));
        g.DrawRectangle(edge, 0.5f, 0.5f, w - 1.5f, h - 1.5f);
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

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            Clicked?.Invoke();
            timeline.Dismiss();
        }
        else if (e.Button == MouseButtons.Right)
        {
            timeline.Dismiss();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            swarm.Dispose();
            buffer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
