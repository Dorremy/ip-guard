using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class NebulaRenderer : IDisposable
{
    public const int LogicalWidth = NebulaLayout.Width;
    public const int LogicalHeight = NebulaLayout.Height;
    private const int CurtainWidth = 75;
    private const int CurtainHeight = 105;
    private const float CurtainRefresh = 0.12f;
    private const float LinkDistance = 60f;

    private static readonly Color Pale = Color.FromArgb(205, 216, 235);

    private readonly int[] curtainPixels = new int[CurtainWidth * CurtainHeight];
    private readonly Bitmap curtainSmall = new(CurtainWidth, CurtainHeight, PixelFormat.Format32bppArgb);
    private Bitmap? curtainFull;
    private float curtainTime = -10f;
    private float curtainDim = -1f;
    private byte[] frame = Array.Empty<byte>();
    private int haloRadius = -1;
    private int[] halo = Array.Empty<int>();
    private float fontScale = -1f;
    private Font? ipFont;
    private Font? subFont;
    private Font? itemFont;
    private Bitmap? infoCache;
    private string infoKey = "";
    private Bitmap? overlay;
    private (int Hover, int Focus, int Pressed, int Width, int Height) overlayKey;
    private Bitmap? glass;
    private GraphicsPath? glassPath;
    private Rectangle glassArea;
    private int linkWidth = 1;

    public static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Alert => Color.FromArgb(255, 190, 120),
        Tone.Offline => Color.FromArgb(160, 166, 178),
        _ => Color.FromArgb(150, 225, 190)
    };

    public void Render(Bitmap target, IntroDirector director, MenuModel menu)
    {
        var w = target.Width;
        var h = target.Height;
        if (w < 8 || h < 8)
        {
            return;
        }

        var sx = w / (float)LogicalWidth;
        var sy = h / (float)LogicalHeight;
        EnsureCurtain(w, h, director.Time, director.Dim);
        using (var g = Graphics.FromImage(target))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(curtainFull!, 0, 0);
        }

        Splat(target, director, sx, sy);
        using (var g = Graphics.FromImage(target))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            EnsureFonts(sx);
            DrawInfo(g, director, sx, sy);
            DrawMenu(g, target, director, menu, sx, sy);
            using var border = new Pen(Color.FromArgb(26, 255, 255, 255), 1f);
            g.DrawRectangle(border, 0.5f, 0.5f, w - 1.5f, h - 1.5f);
        }
    }

    public void Dispose()
    {
        curtainSmall.Dispose();
        curtainFull?.Dispose();
        ipFont?.Dispose();
        subFont?.Dispose();
        itemFont?.Dispose();
        infoCache?.Dispose();
        overlay?.Dispose();
        glass?.Dispose();
        glassPath?.Dispose();
    }

    private void EnsureCurtain(int w, int h, float time, float dim)
    {
        var resized = curtainFull is null || curtainFull.Width != w || curtainFull.Height != h;
        if (!resized && Math.Abs(time - curtainTime) < CurtainRefresh && Math.Abs(dim - curtainDim) < 0.005f)
        {
            return;
        }

        if (resized)
        {
            curtainFull?.Dispose();
            curtainFull = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }

        Curtain.Fill(MemoryMarshal.Cast<int, uint>(curtainPixels.AsSpan()), CurtainWidth, CurtainHeight, time, dim);
        var data = curtainSmall.LockBits(new Rectangle(0, 0, CurtainWidth, CurtainHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (var y = 0; y < CurtainHeight; y++)
        {
            Marshal.Copy(curtainPixels, y * CurtainWidth, data.Scan0 + y * data.Stride, CurtainWidth);
        }

        curtainSmall.UnlockBits(data);
        using (var g = Graphics.FromImage(curtainFull!))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(curtainSmall, new Rectangle(0, 0, w, h), 0, 0, CurtainWidth, CurtainHeight, GraphicsUnit.Pixel, attributes);
        }

        curtainTime = time;
        curtainDim = dim;
    }

    private void EnsureFonts(float scale)
    {
        if (Math.Abs(scale - fontScale) < 0.001f)
        {
            return;
        }

        ipFont?.Dispose();
        subFont?.Dispose();
        itemFont?.Dispose();
        ipFont = new Font("Segoe UI Light", 26f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        subFont = new Font("Segoe UI", 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        itemFont = new Font("Segoe UI", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        fontScale = scale;
    }

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

    private void Splat(Bitmap target, IntroDirector d, float sx, float sy)
    {
        EnsureHalo(sx);
        var w = target.Width;
        var h = target.Height;
        var data = target.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var stride = Math.Abs(data.Stride);
        var needed = stride * h;
        if (frame.Length < needed)
        {
            frame = new byte[needed];
        }

        Marshal.Copy(data.Scan0, frame, 0, needed);
        var f = d.Field;
        var time = d.Time;
        var life = Smooth(time / 0.5f);
        var flash = d.Flash;
        var toneColor = ToneColor(d.Tone);
        var fromColor = d.PreviousTone is { } previous ? ToneColor(previous) : Pale;
        var statusColor = Mix(fromColor, toneColor, d.Blend);
        var waveSpeed = d.Checking ? 330f : 140f;
        var dust = d.DustLevel;
        var radius = haloRadius;
        var size = radius * 2 + 1;
        for (var i = 0; i < f.Count; i++)
        {
            var b = f.Brightness[i] * life;
            if (b <= 0.01f)
            {
                continue;
            }

            var status = i < IntroDirector.StatusCount;
            var driven = f.Driven(i);
            var c = Pale;
            if (status)
            {
                c = statusColor;
                if (flash > 0f)
                {
                    c = Mix(c, Color.White, flash * 0.7f);
                    b *= 1f + flash * 0.8f;
                }

                if (driven)
                {
                    b *= 0.75f + 0.25f * MathF.Sin((f.X[i] * 2f - time * waveSpeed) / 55f);
                }
            }
            else if (d.IsDust(i))
            {
                b *= dust;
            }

            if (!driven)
            {
                var edge = MathF.Min(MathF.Min(f.X[i], f.Width - f.X[i]), MathF.Min(f.Y[i], f.Height - f.Y[i]));
                if (edge < 14f)
                {
                    b *= Smooth(edge / 14f);
                }
            }

            var px = f.X[i] * sx;
            var py = f.Y[i] * sy;
            if (driven)
            {
                var shimmer = status ? 0.18f : 0.35f;
                px += shimmer * sx * MathF.Sin(time * 2.1f + i);
                py += shimmer * sy * MathF.Cos(time * 1.7f + i * 1.3f);
            }

            var x0 = (int)MathF.Floor(px);
            var y0 = (int)MathF.Floor(py);
            var fx = px - x0;
            var fy = py - y0;
            var r = c.R * b;
            var gr = c.G * b;
            var bl = c.B * b;
            AddPixel(x0, y0, (1 - fx) * (1 - fy), r, gr, bl, w, h, stride);
            AddPixel(x0 + 1, y0, fx * (1 - fy), r, gr, bl, w, h, stride);
            AddPixel(x0, y0 + 1, (1 - fx) * fy, r, gr, bl, w, h, stride);
            AddPixel(x0 + 1, y0 + 1, fx * fy, r, gr, bl, w, h, stride);
            var haloGain = status && driven ? 0.22f : 0.35f;
            var hr = (int)(r * haloGain);
            var hg = (int)(gr * haloGain);
            var hb = (int)(bl * haloGain);
            var cx = (int)MathF.Round(px);
            var cy = (int)MathF.Round(py);
            for (var ky = 0; ky < size; ky++)
            {
                var yy = cy + ky - radius;
                if (yy < 0 || yy >= h)
                {
                    continue;
                }

                for (var kx = 0; kx < size; kx++)
                {
                    var k = halo[ky * size + kx];
                    var xx = cx + kx - radius;
                    if (k == 0 || xx < 0 || xx >= w)
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

        DrawLinks(d, sx, sy, w, h, stride);
        Marshal.Copy(frame, 0, data.Scan0, needed);
        target.UnlockBits(data);
    }

    private void AddPixel(int x, int y, float weight, float r, float g, float b, int w, int h, int stride)
    {
        if (x < 0 || y < 0 || x >= w || y >= h || weight <= 0f)
        {
            return;
        }

        var idx = y * stride + x * 4;
        frame[idx] = Sat(frame[idx] + (int)(b * weight));
        frame[idx + 1] = Sat(frame[idx + 1] + (int)(g * weight));
        frame[idx + 2] = Sat(frame[idx + 2] + (int)(r * weight));
    }

    private static byte Sat(int v) => v > 255 ? (byte)255 : (byte)v;

    private void DrawLinks(IntroDirector d, float sx, float sy, int w, int h, int stride)
    {
        var level = d.LinkLevel;
        if (level <= 0.01f)
        {
            return;
        }

        var f = d.Field;
        var roam = d.Roamers;
        var limit = LinkDistance * sx;
        linkWidth = sx >= 1.5f ? 2 : 1;
        for (var a = 0; a < roam.Count; a++)
        {
            var ax = f.X[roam[a]] * sx;
            var ay = f.Y[roam[a]] * sy;
            for (var b = a + 1; b < roam.Count; b++)
            {
                var dx = f.X[roam[b]] * sx - ax;
                var dy = f.Y[roam[b]] * sy - ay;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist >= limit)
                {
                    continue;
                }

                var alpha = (int)(130f * (1f - dist / limit) * level);
                if (alpha < 4)
                {
                    continue;
                }

                Line(ax, ay, dx, dy, alpha, w, h, stride);
            }
        }
    }

    private void Line(float x0, float y0, float dx, float dy, int alpha, int w, int h, int stride)
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
            for (var t = 0; t < linkWidth; t++)
            {
                var px = (int)x;
                var py = (int)y + t;
                if (px < 0 || py < 0 || px >= w || py >= h)
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

    private void DrawInfo(Graphics g, IntroDirector d, float sx, float sy)
    {
        var alpha = d.InfoAlpha;
        if (alpha <= 0.01f)
        {
            return;
        }

        var state = d.State;
        var width = Math.Max(1, (int)((NebulaLayout.Width - 20) * sx));
        var height = Math.Max(1, (int)(60 * sy));
        var key = $"{state.Ip}|{state.Country}|{state.Provider}|{width}x{height}";
        if (infoCache is null || key != infoKey)
        {
            infoCache?.Dispose();
            infoCache = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            infoKey = key;
            using var ig = Graphics.FromImage(infoCache);
            ig.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var format = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            using (var brush = new SolidBrush(Color.FromArgb(235, 236, 240, 248)))
            {
                ig.DrawString(PanelState.Fit(state.Ip, 24), ipFont!, brush, new RectangleF(0, 0, width, 30 * sy), format);
            }

            var place = string.Join("  ·  ", new[] { state.Country, state.Provider }.Where(s => !string.IsNullOrWhiteSpace(s)));
            using (var brush = new SolidBrush(Color.FromArgb(220, 150, 160, 180)))
            {
                ig.DrawString(PanelState.Fit(place, 44), subFont!, brush, new RectangleF(0, 39 * sy, width, 20 * sy), format);
            }
        }

        DrawFaded(g, infoCache, new Rectangle((int)(10 * sx), (int)((NebulaLayout.IpY - 15) * sy), width, height), alpha);
    }

    private static void DrawFaded(Graphics g, Bitmap image, Rectangle dest, float alpha)
    {
        if (alpha >= 0.995f)
        {
            g.DrawImageUnscaled(image, dest.Location);
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(image, dest, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    private void DrawMenu(Graphics g, Bitmap target, IntroDirector d, MenuModel menu, float sx, float sy)
    {
        var p = d.MenuProgress;
        var logical = menu.Area(p);
        if (logical.IsEmpty)
        {
            return;
        }

        var area = new Rectangle((int)(logical.X * sx), (int)(logical.Y * sy), Math.Max(1, (int)(logical.Width * sx)), Math.Max(1, (int)(logical.Height * sy)));
        if (glassPath is null || glassArea != area)
        {
            glassPath?.Dispose();
            glassPath = Rounded(area, 13f * sx);
            glassArea = area;
        }

        var sw = Math.Max(1, area.Width / 6);
        var sh = Math.Max(1, area.Height / 6);
        if (glass is null || glass.Width != sw || glass.Height != sh)
        {
            glass?.Dispose();
            glass = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        }

        using (var sg = Graphics.FromImage(glass))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(target, new Rectangle(0, 0, sw, sh), area.X, area.Y, area.Width, area.Height, GraphicsUnit.Pixel);
        }

        g.SetClip(glassPath);
        g.DrawImage(glass, area);
        if (p >= 1f)
        {
            var key = (menu.Hover, menu.Focus, menu.Pressed, area.Width, area.Height);
            if (overlay is null || key != overlayKey)
            {
                overlay?.Dispose();
                overlay = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                overlayKey = key;
                using var og = Graphics.FromImage(overlay);
                og.SmoothingMode = SmoothingMode.AntiAlias;
                og.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawGlassSkin(og, new RectangleF(0, 0, area.Width, area.Height), sx);
                DrawItems(og, menu, 1f, sx, sy, new PointF(-area.X, -area.Y));
            }

            g.DrawImageUnscaled(overlay, area.Location);
        }
        else
        {
            DrawGlassSkin(g, area, sx);
            DrawItems(g, menu, p, sx, sy, PointF.Empty);
        }

        g.ResetClip();
    }

    private static void DrawGlassSkin(Graphics g, RectangleF area, float sx)
    {
        using var path = Rounded(area, 13f * sx);
        using (var fill = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
        {
            g.FillPath(fill, path);
        }

        using var edge = new Pen(Color.FromArgb(44, 255, 255, 255), Math.Max(1f, sx));
        g.DrawPath(edge, path);
    }

    private void DrawItems(Graphics g, MenuModel menu, float progress, float sx, float sy, PointF offset)
    {
        using var format = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        for (var i = 0; i < menu.Items.Count; i++)
        {
            var a = menu.ItemAlpha(i, progress);
            if (a <= 0.01f)
            {
                continue;
            }

            var b = menu.ItemBounds(i);
            var lift = menu.Pressed == i ? sy : 0f;
            var rect = new RectangleF(b.X * sx + offset.X, b.Y * sy + lift + offset.Y, b.Width * sx, b.Height * sy);
            var strength = 22 + (menu.Hover == i ? 18 : 0) + (menu.Pressed == i ? 10 : 0) + (menu.Focus == i ? 8 : 0);
            using (var item = Rounded(rect, 10f * sx))
            {
                using (var fillItem = new SolidBrush(Color.FromArgb((int)(strength * a), 255, 255, 255)))
                {
                    g.FillPath(fillItem, item);
                }

                using var line = new Pen(Color.FromArgb((int)((menu.Focus == i ? 170 : 54) * a), 255, 255, 255), Math.Max(1f, menu.Focus == i ? 1.5f * sx : sx));
                g.DrawPath(line, item);
            }

            using var text = new SolidBrush(Color.FromArgb((int)(240 * a), 238, 240, 246));
            g.DrawString(menu.Items[i].Text, itemFont!, text, new RectangleF(rect.X + 14 * sx, rect.Y, rect.Width - 34 * sx, rect.Height), format);
            using var chevron = new SolidBrush(Color.FromArgb((int)(190 * a), 170, 180, 200));
            g.DrawString("›", itemFont!, chevron, new RectangleF(rect.X, rect.Y, rect.Width - 12 * sx, rect.Height), right);
        }
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Mix(Color a, Color b, float t)
    {
        var k = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    private static float Smooth(float x)
    {
        var t = Math.Clamp(x, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
