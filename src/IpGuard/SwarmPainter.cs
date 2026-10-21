using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IpGuard;

public readonly struct Dot
{
    public Dot(float brightness, Color color, float halo, float shimmer)
    {
        Brightness = brightness;
        Color = color;
        Halo = halo;
        Shimmer = shimmer;
    }

    public float Brightness { get; }

    public Color Color { get; }

    public float Halo { get; }

    public float Shimmer { get; }
}

public delegate Dot DotStyle(int index);

public sealed class SwarmPainter
{
    private byte[] frame = Array.Empty<byte>();
    private int haloRadius = -1;
    private int[] halo = Array.Empty<int>();
    private Bitmap? target;
    private BitmapData? data;
    private int width;
    private int height;
    private int stride;
    private int bytes;

    public void Begin(Bitmap bitmap, float scale)
    {
        EnsureHalo(scale);
        target = bitmap;
        width = bitmap.Width;
        height = bitmap.Height;
        data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        stride = Math.Abs(data.Stride);
        bytes = stride * height;
        if (frame.Length < bytes)
        {
            frame = new byte[bytes];
        }

        Marshal.Copy(data.Scan0, frame, 0, bytes);
    }

    public void End()
    {
        if (target is null || data is null)
        {
            return;
        }

        Marshal.Copy(frame, 0, data.Scan0, bytes);
        target.UnlockBits(data);
        target = null;
        data = null;
    }

    public void Splat(ParticleField f, float sx, float sy, float time, DotStyle style)
    {
        var radius = haloRadius;
        var size = radius * 2 + 1;
        for (var i = 0; i < f.Count; i++)
        {
            var dot = style(i);
            var b = dot.Brightness;
            if (b <= 0.01f)
            {
                continue;
            }

            var px = f.X[i] * sx;
            var py = f.Y[i] * sy;
            if (dot.Shimmer > 0f)
            {
                px += dot.Shimmer * sx * MathF.Sin(time * 2.1f + i);
                py += dot.Shimmer * sy * MathF.Cos(time * 1.7f + i * 1.3f);
            }

            var x0 = (int)MathF.Floor(px);
            var y0 = (int)MathF.Floor(py);
            var fx = px - x0;
            var fy = py - y0;
            var r = dot.Color.R * b;
            var gr = dot.Color.G * b;
            var bl = dot.Color.B * b;
            AddPixel(x0, y0, (1 - fx) * (1 - fy), r, gr, bl);
            AddPixel(x0 + 1, y0, fx * (1 - fy), r, gr, bl);
            AddPixel(x0, y0 + 1, (1 - fx) * fy, r, gr, bl);
            AddPixel(x0 + 1, y0 + 1, fx * fy, r, gr, bl);
            var hr = (int)(r * dot.Halo);
            var hg = (int)(gr * dot.Halo);
            var hb = (int)(bl * dot.Halo);
            var cx = (int)MathF.Round(px);
            var cy = (int)MathF.Round(py);
            for (var ky = 0; ky < size; ky++)
            {
                var yy = cy + ky - radius;
                if (yy < 0 || yy >= height)
                {
                    continue;
                }

                for (var kx = 0; kx < size; kx++)
                {
                    var k = halo[ky * size + kx];
                    var xx = cx + kx - radius;
                    if (k == 0 || xx < 0 || xx >= width)
                    {
                        continue;
                    }

                    var idx = yy * stride + xx * 4;
                    frame[idx] = Sat(frame[idx] + ((hb * k) >> 8));
                    frame[idx + 1] = Sat(frame[idx + 1] + ((hg * k) >> 8));
                    frame[idx + 2] = Sat(frame[idx + 2] + ((hr * k) >> 8));
                }
            }
        }
    }

    public void Line(float x0, float y0, float dx, float dy, int alpha, int thickness)
    {
        var steps = (int)MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
        if (steps < 1)
        {
            return;
        }

        var sxs = dx / steps;
        var sys = dy / steps;
        var x = x0;
        var y = y0;
        for (var i = 0; i <= steps; i++, x += sxs, y += sys)
        {
            for (var t = 0; t < thickness; t++)
            {
                var px = (int)x;
                var py = (int)y + t;
                if (px < 0 || py < 0 || px >= width || py >= height)
                {
                    continue;
                }

                var idx = py * stride + px * 4;
                frame[idx] = (byte)(frame[idx] + (((238 - frame[idx]) * alpha) >> 8));
                frame[idx + 1] = (byte)(frame[idx + 1] + (((208 - frame[idx + 1]) * alpha) >> 8));
                frame[idx + 2] = (byte)(frame[idx + 2] + (((190 - frame[idx + 2]) * alpha) >> 8));
            }
        }
    }

    private void AddPixel(int x, int y, float weight, float r, float g, float b)
    {
        if (x < 0 || y < 0 || x >= width || y >= height || weight <= 0f)
        {
            return;
        }

        var idx = y * stride + x * 4;
        frame[idx] = Sat(frame[idx] + (int)(b * weight));
        frame[idx + 1] = Sat(frame[idx + 1] + (int)(g * weight));
        frame[idx + 2] = Sat(frame[idx + 2] + (int)(r * weight));
    }

    private static byte Sat(int v) => v > 255 ? (byte)255 : (byte)v;

    private void EnsureHalo(float scale)
    {
        var radius = Math.Clamp((int)MathF.Round(3.2f * scale), 2, 7);
        if (radius == haloRadius)
        {
            return;
        }

        haloRadius = radius;
        var size = radius * 2 + 1;
        halo = new int[size * size];
        var sigma = radius / 2.2f;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var r2 = dx * dx + dy * dy;
                halo[(dy + radius) * size + dx + radius] = r2 > radius * radius ? 0 : (int)(256f * MathF.Exp(-r2 / (2 * sigma * sigma)));
            }
        }
    }
}
