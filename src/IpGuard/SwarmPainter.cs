using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IpGuard;

public readonly struct Dot
{
    public Dot(float brightness, Color color, float halo, float shimmer, float core = 1f)
    {
        Brightness = brightness;
        Color = color;
        Halo = halo;
        Shimmer = shimmer;
        Core = core;
    }

    public float Brightness { get; }

    public Color Color { get; }

    public float Halo { get; }

    public float Shimmer { get; }

    public float Core { get; }
}

public delegate Dot DotStyle(int index);

public sealed class SwarmPainter
{
    private const int MinStamp = 10;
    private const int MinKernel = 10;

    private byte[] frame = Array.Empty<byte>();
    private int haloRadius = -1;
    private const int Phases = 4;

    private int[][] halos = Array.Empty<int[]>();
    private int bokehRadius = -1;
    private int[] bokeh = Array.Empty<int>();
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
        for (var i = 0; i < f.Count; i++)
        {
            var dot = style(i);
            if (dot.Brightness <= 0.01f)
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

            Spot(px, py, dot);
        }
    }

    public void Spot(float px, float py, Dot dot)
    {
        var b = dot.Brightness;
        if (b <= 0.01f || !float.IsFinite(px) || !float.IsFinite(py))
        {
            return;
        }

        var radius = haloRadius;
        var size = radius * 2 + 1;
        var x0 = (int)MathF.Floor(px);
        var y0 = (int)MathF.Floor(py);
        var fx = px - x0;
        var fy = py - y0;
        var r = dot.Color.R * b;
        var gr = dot.Color.G * b;
        var bl = dot.Color.B * b;
        if (dot.Core > 0f)
        {
            var c = dot.Core;
            AddPixel(x0, y0, (1 - fx) * (1 - fy) * c, r, gr, bl);
            AddPixel(x0 + 1, y0, fx * (1 - fy) * c, r, gr, bl);
            AddPixel(x0, y0 + 1, (1 - fx) * fy * c, r, gr, bl);
            AddPixel(x0 + 1, y0 + 1, fx * fy * c, r, gr, bl);
        }

        var cx = (int)MathF.Round(px);
        var cy = (int)MathF.Round(py);
        var kernel = halos[Phase(py - cy) * Phases + Phase(px - cx)];
        Stamp(kernel, radius, size, cx, cy, (int)(r * dot.Halo), (int)(gr * dot.Halo), (int)(bl * dot.Halo));
    }

    public void AddTrail(float[] data, int w, int h, Color color, float gain)
    {
        if (target is null || w != width || h != height || data.Length < w * h)
        {
            return;
        }

        var cr = color.R * gain;
        var cg = color.G * gain;
        var cb = color.B * gain;
        for (var y = 0; y < h; y++)
        {
            var row = y * stride;
            var src = y * w;
            for (var x = 0; x < w; x++)
            {
                var v = data[src + x];
                if (v < 0.012f)
                {
                    continue;
                }

                if (v > 1.5f)
                {
                    v = 1.5f;
                }

                var idx = row + x * 4;
                frame[idx] = Sat(frame[idx] + (int)(cb * v));
                frame[idx + 1] = Sat(frame[idx + 1] + (int)(cg * v));
                frame[idx + 2] = Sat(frame[idx + 2] + (int)(cr * v));
            }
        }
    }

    public void Bokeh(float px, float py, float scale, float brightness, Color color)
    {
        if (brightness <= 0.005f || !float.IsFinite(px) || !float.IsFinite(py))
        {
            return;
        }

        EnsureBokeh(scale);
        var size = bokehRadius * 2 + 1;
        Stamp(bokeh, bokehRadius, size, (int)MathF.Round(px), (int)MathF.Round(py), (int)(color.R * brightness), (int)(color.G * brightness), (int)(color.B * brightness));
    }

    private void Stamp(int[] kernel, int radius, int size, int cx, int cy, int hr, int hg, int hb)
    {
        if (Math.Max(hr, Math.Max(hg, hb)) < MinStamp)
        {
            return;
        }

        for (var ky = 0; ky < size; ky++)
        {
            var yy = cy + ky - radius;
            if (yy < 0 || yy >= height)
            {
                continue;
            }

            for (var kx = 0; kx < size; kx++)
            {
                var k = kernel[ky * size + kx];
                var xx = cx + kx - radius;
                if (k < MinKernel || xx < 0 || xx >= width)
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

    private void EnsureBokeh(float scale)
    {
        var radius = Math.Clamp((int)MathF.Round(8f * scale), 4, 18);
        if (radius == bokehRadius)
        {
            return;
        }

        bokehRadius = radius;
        var size = radius * 2 + 1;
        bokeh = new int[size * size];
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var d = MathF.Sqrt(dx * dx + dy * dy) / radius;
                var v = d >= 1f ? 0f : (1f - d * d) * (0.75f + 0.25f * MathF.Pow(d, 4f));
                bokeh[(dy + radius) * size + dx + radius] = (int)(256f * v);
            }
        }
    }

    public void Line(float x0, float y0, float dx, float dy, int alpha, int thickness)
    {
        var steep = MathF.Abs(dy) > MathF.Abs(dx);
        var steps = (int)MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
        if (steps < 1 || alpha <= 0)
        {
            return;
        }

        var sxs = dx / steps;
        var sys = dy / steps;
        var x = x0;
        var y = y0;
        for (var i = 0; i <= steps; i++, x += sxs, y += sys)
        {
            var edge = i == 0 || i == steps ? 0.5f : 1f;
            for (var t = 0; t < thickness; t++)
            {
                if (steep)
                {
                    var fx = x + t;
                    var ix = (int)MathF.Floor(fx);
                    var f = fx - ix;
                    Blend(ix, (int)y, (int)(alpha * (1f - f) * edge));
                    Blend(ix + 1, (int)y, (int)(alpha * f * edge));
                }
                else
                {
                    var fy = y + t;
                    var iy = (int)MathF.Floor(fy);
                    var f = fy - iy;
                    Blend((int)x, iy, (int)(alpha * (1f - f) * edge));
                    Blend((int)x, iy + 1, (int)(alpha * f * edge));
                }
            }
        }
    }

    private void Blend(int px, int py, int alpha)
    {
        if (alpha <= 0 || px < 0 || py < 0 || px >= width || py >= height)
        {
            return;
        }

        var idx = py * stride + px * 4;
        frame[idx] = (byte)(frame[idx] + (((238 - frame[idx]) * alpha) >> 8));
        frame[idx + 1] = (byte)(frame[idx + 1] + (((208 - frame[idx + 1]) * alpha) >> 8));
        frame[idx + 2] = (byte)(frame[idx + 2] + (((190 - frame[idx + 2]) * alpha) >> 8));
    }

    private static int Phase(float offset) => Math.Clamp((int)((offset + 0.5f) * Phases), 0, Phases - 1);

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
        var radius = Math.Clamp((int)MathF.Round(2.6f * scale), 2, 6);
        if (radius == haloRadius)
        {
            return;
        }

        haloRadius = radius;
        var size = radius * 2 + 1;
        var sigma = radius / 2.2f;
        var reach = (radius + 0.5f) * (radius + 0.5f);
        halos = new int[Phases * Phases][];
        for (var qy = 0; qy < Phases; qy++)
        {
            for (var qx = 0; qx < Phases; qx++)
            {
                var ox = (qx + 0.5f) / Phases - 0.5f;
                var oy = (qy + 0.5f) / Phases - 0.5f;
                var kernel = new int[size * size];
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var ex = dx - ox;
                        var ey = dy - oy;
                        var r2 = ex * ex + ey * ey;
                        kernel[(dy + radius) * size + dx + radius] = r2 > reach ? 0 : (int)(256f * MathF.Exp(-r2 / (2 * sigma * sigma)));
                    }
                }

                halos[qy * Phases + qx] = kernel;
            }
        }
    }
}
