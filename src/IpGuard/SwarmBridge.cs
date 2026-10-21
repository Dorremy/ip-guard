using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class SwarmBridge : Form
{
    private const int WsExLayered = 0x80000;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int UlwAlpha = 2;
    private const int AcSrcOver = 0;
    private const int AcSrcAlpha = 1;
    private const int MaxStep = 100;

    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly Stopwatch clock = new();
    private readonly BridgeSwarm swarm = new(Environment.TickCount);
    private Rectangle panel;
    private Rectangle window;
    private byte[] pixels = Array.Empty<byte>();
    private Size surface;
    private IntPtr dib;
    private IntPtr dibBits;
    private double last;
    private float scale = 1f;
    private Color tint = Color.FromArgb(200, 215, 235);

    public SwarmBridge()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        timer.Tick += (_, _) => Advance(Elapsed());
    }

    public BridgeSwarm Swarm => swarm;

    public int ExtendedStyle => CreateParams.ExStyle;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = WsPopup;
            cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    public static Rectangle BoundsFor(Rectangle panel, Rectangle window, float scale)
    {
        var margin = (int)Math.Ceiling(BridgeSwarm.Reach * Math.Max(0.75f, scale));
        var union = Rectangle.Union(panel, window);
        union.Inflate(margin, margin);
        return union;
    }

    public void SetTone(Tone tone)
    {
        tint = Gfx.Mix(Color.FromArgb(200, 215, 235), NebulaRenderer.ToneColor(tone), 0.3f);
    }

    public void Begin(Rectangle panelBounds, Rectangle windowBounds)
    {
        Track(panelBounds, windowBounds);
        swarm.Open();
        if (!Visible)
        {
            Show();
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        last = 0;
        clock.Restart();
        timer.Start();
        Advance(0.016f);
    }

    public void Track(Rectangle panelBounds, Rectangle windowBounds)
    {
        panel = panelBounds;
        window = windowBounds;
        var bounds = BoundsFor(panel, window, scale);
        if (bounds != Bounds)
        {
            Bounds = bounds;
        }
    }

    public void Finish()
    {
        swarm.Close();
    }

    public void Advance(float dt)
    {
        if (!Visible)
        {
            return;
        }

        var bounds = Bounds;
        swarm.SetRects(
            new RectangleF(panel.X, panel.Y, panel.Width, panel.Height),
            new RectangleF(window.X, window.Y, window.Width, window.Height));
        swarm.Step(Math.Min(dt, MaxStep / 1000f));
        if (swarm.Finished)
        {
            timer.Stop();
            clock.Stop();
            Hide();
            return;
        }

        Present(bounds);
    }

    private float Elapsed()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - last, 0, 0.25);
        last = now;
        return dt;
    }

    private void Present(Rectangle bounds)
    {
        if (!IsHandleCreated || bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        EnsureSurface(bounds.Size);
        if (dib == IntPtr.Zero)
        {
            return;
        }

        BridgeRenderer.Render(pixels, bounds.Width, bounds.Height, swarm, bounds.X, bounds.Y, scale, tint);
        Marshal.Copy(pixels, 0, dibBits, bounds.Width * bounds.Height * 4);
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var old = SelectObject(memory, dib);
        var size = new Win32Size { Width = bounds.Width, Height = bounds.Height };
        var source = new Win32Point();
        var position = new Win32Point { X = bounds.X, Y = bounds.Y };
        var blend = new Blend { Op = AcSrcOver, Flags = 0, Alpha = 255, Format = AcSrcAlpha };
        UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        SelectObject(memory, old);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
    }

    private void EnsureSurface(Size size)
    {
        if (dib != IntPtr.Zero && surface == size)
        {
            return;
        }

        ReleaseSurface();
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = size.Width,
            Height = -size.Height,
            Planes = 1,
            BitCount = 32
        };
        dib = CreateDIBSection(IntPtr.Zero, ref header, 0, out dibBits, IntPtr.Zero, 0);
        surface = size;
        pixels = new byte[size.Width * size.Height * 4];
    }

    private void ReleaseSurface()
    {
        if (dib != IntPtr.Zero)
        {
            DeleteObject(dib);
            dib = IntPtr.Zero;
            dibBits = IntPtr.Zero;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
        }

        ReleaseSurface();
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Blend
    {
        public byte Op;
        public byte Flags;
        public byte Alpha;
        public byte Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Win32Point pptDst, ref Win32Size psize, IntPtr hdcSrc, ref Win32Point pptSrc, int crKey, ref Blend pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
