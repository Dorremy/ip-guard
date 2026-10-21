using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class CurtainCache : IDisposable
{
    private const float Refresh = 0.12f;

    private readonly int gridWidth;
    private readonly int gridHeight;
    private readonly int[] pixels;
    private readonly Bitmap small;
    private Bitmap? full;
    private float cachedTime = -10f;
    private float cachedDim = -1f;

    public CurtainCache(int gridWidth, int gridHeight)
    {
        this.gridWidth = gridWidth;
        this.gridHeight = gridHeight;
        pixels = new int[gridWidth * gridHeight];
        small = new Bitmap(gridWidth, gridHeight, PixelFormat.Format32bppArgb);
    }

    public void Draw(Graphics g, int w, int h, float time, float dim)
    {
        Ensure(w, h, time, dim);
        var mode = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(full!, 0, 0);
        g.CompositingMode = mode;
    }

    public void Dispose()
    {
        small.Dispose();
        full?.Dispose();
    }

    private void Ensure(int w, int h, float time, float dim)
    {
        var resized = full is null || full.Width != w || full.Height != h;
        if (!resized && Math.Abs(time - cachedTime) < Refresh && Math.Abs(dim - cachedDim) < 0.005f)
        {
            return;
        }

        if (resized)
        {
            full?.Dispose();
            full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }

        Curtain.Fill(MemoryMarshal.Cast<int, uint>(pixels.AsSpan()), gridWidth, gridHeight, time, dim);
        var data = small.LockBits(new Rectangle(0, 0, gridWidth, gridHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (var y = 0; y < gridHeight; y++)
        {
            Marshal.Copy(pixels, y * gridWidth, data.Scan0 + y * data.Stride, gridWidth);
        }

        small.UnlockBits(data);
        using (var g = Graphics.FromImage(full!))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(small, new Rectangle(0, 0, w, h), 0, 0, gridWidth, gridHeight, GraphicsUnit.Pixel, attributes);
        }

        cachedTime = time;
        cachedDim = dim;
    }
}
