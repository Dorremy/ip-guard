using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IpGuard;

public sealed class NebulaRenderer : IDisposable
{
    public const int LogicalWidth = NebulaLayout.Width;
    public const int LogicalHeight = NebulaLayout.Height;
    private const float TrailKeep = 0.972f;
    private const float TrailWeight = 0.28f;
    private const float TrailGain = 0.9f;
    private const float ArriveDistance = 4f;
    private const float BokehDepth = 0.975f;

    private static readonly Color Pale = Color.FromArgb(205, 216, 235);

    private readonly CurtainCache curtain = new(75, 105);
    private readonly SwarmPainter painter = new();
    private readonly TrailLayer trail = new();
    private float lastTime;
    private float sceneOffset;
    private readonly Random sceneRandom = new();
    private readonly PageLayer pageLayer = new();
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
    private Tone accentTone = Tone.Calm;

    public static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Alert => Color.FromArgb(255, 190, 120),
        Tone.Offline => Color.FromArgb(160, 166, 178),
        _ => Color.FromArgb(150, 225, 190)
    };

    public void Render(Bitmap target, IntroDirector director, MenuModel menu, PageHost? pages = null, bool caretOn = true)
    {
        var w = target.Width;
        var h = target.Height;
        if (w < 8 || h < 8)
        {
            return;
        }

        var sx = w / (float)LogicalWidth;
        var sy = h / (float)LogicalHeight;
        var elapsed = director.Time - lastTime;
        lastTime = director.Time;
        trail.Resize(w, h);
        if (elapsed < 0f || elapsed > 0.5f)
        {
            trail.Clear();
            sceneOffset = 40f + (float)sceneRandom.NextDouble() * 4000f;
            elapsed = 0f;
        }

        using (var g = Graphics.FromImage(target))
        {
            curtain.Draw(g, w, h, director.Time + sceneOffset, director.Dim, director.Warm, director.Glow);
        }

        Splat(target, director, sx, sy, elapsed);
        using (var g = Graphics.FromImage(target))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            EnsureFonts(sx);
            var pageProgress = pages?.Progress ?? 0f;
            DrawInfo(g, director, sx, sy, 1f - pageProgress);
            DrawMenu(g, target, director, menu, pages, caretOn, sx, sy);
            using var border = new Pen(Color.FromArgb(26, 255, 255, 255), 1f);
            g.DrawRectangle(border, 0.5f, 0.5f, w - 1.5f, h - 1.5f);
        }
    }

    public void Dispose()
    {
        curtain.Dispose();
        pageLayer.Dispose();
        ipFont?.Dispose();
        subFont?.Dispose();
        itemFont?.Dispose();
        infoCache?.Dispose();
        overlay?.Dispose();
        glass?.Dispose();
        glassPath?.Dispose();
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

    private void Splat(Bitmap target, IntroDirector d, float sx, float sy, float elapsed)
    {
        painter.Begin(target, sx);
        var f = d.Field;
        var time = d.Time;
        var life = Smooth(time / 0.5f);
        var flash = d.Flash;
        accentTone = d.Tone;
        var toneColor = ToneColor(d.Tone);
        var fromColor = d.PreviousTone is { } previous ? ToneColor(previous) : Pale;
        var statusColor = Mix(fromColor, toneColor, d.Blend);
        var waveSpeed = d.Checking ? 330f : 140f;
        var dust = d.DustLevel;
        trail.Decay(MathF.Pow(TrailKeep, elapsed * 30f));

        var info = d.InfoAlpha;
        var crowd = d.Crowd;
        var calm = 0.55f + 0.45f * crowd;
        var trailColor = Mix(Pale, statusColor, 0.4f);
        painter.Splat(f, sx, sy, time, i =>
        {
            var b = f.Brightness[i] * life * (f.Driven(i) ? 1f : IntroDirector.Presence(i, crowd));
            if (b <= 0.01f)
            {
                return default;
            }

            var status = i < IntroDirector.StatusCount;
            var driven = f.Driven(i);
            var depth = f.Depth[i];
            var loose = !(status && driven);
            if (loose)
            {
                b *= (0.3f + 0.7f * depth) * (status ? 0.7f : 1f);
                if (depth >= BokehDepth && !status)
                {
                    return default;
                }
            }

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

            if (status && driven && Arrived(f, i))
            {
                return new Dot(b, c, 0.22f, 0.18f);
            }

            var zone = status ? 1f : 1f - 0.7f * info * InStatusBand(f.Y[i]);
            trail.Deposit(f.X[i] * sx, f.Y[i] * sy, b * TrailWeight * zone * (status ? 1.5f : 1f) * (depth < 0.5f ? 1f : calm));
            return new Dot(b * 0.3f * zone, c, 0.08f, 0f);
        });
        painter.AddTrail(trail.Data, trail.Width, trail.Height, trailColor, TrailGain);
        DrawBokeh(d, sx, sy, life * dust * crowd * crowd);
        painter.End();
    }

    private void DrawBokeh(IntroDirector d, float sx, float sy, float level)
    {
        var f = d.Field;
        for (var i = IntroDirector.StatusCount; i < f.Count; i++)
        {
            if (f.Depth[i] < BokehDepth)
            {
                continue;
            }

            var px = (f.X[i] + 5f * MathF.Sin(d.Time * 0.2f + i)) * sx;
            var py = (f.Y[i] + 5f * MathF.Cos(d.Time * 0.17f + i * 1.3f)) * sy;
            painter.Bokeh(px, py, sx, 0.11f * level * f.Brightness[i], Pale);
        }
    }

    private static bool Arrived(ParticleField f, int i)
    {
        var t = f.TargetOf(i);
        var dx = t.X - f.X[i];
        var dy = t.Y - f.Y[i];
        return dx * dx + dy * dy < ArriveDistance * ArriveDistance;
    }

    private static float InStatusBand(float y) => 1f - Smooth((y - NebulaLayout.PlaceY - 22f) / 24f);

    private void DrawInfo(Graphics g, IntroDirector d, float sx, float sy, float fade)
    {
        var alpha = d.InfoAlpha * fade;
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

    private static void DrawFaded(Graphics g, Bitmap image, Rectangle dest, float alpha) => Gfx.DrawFaded(g, image, dest, alpha);

    private void DrawMenu(Graphics g, Bitmap target, IntroDirector d, MenuModel menu, PageHost? pages, bool caretOn, float sx, float sy)
    {
        var pageP = pages?.Progress ?? 0f;
        var p = d.MenuProgress;
        var menuArea = menu.Area(p);
        Rectangle logical;
        if (pageP > 0.001f)
        {
            logical = menuArea.IsEmpty ? PageGeometry.Area : LerpRect(menuArea, PageGeometry.Area, pageP);
        }
        else
        {
            logical = menuArea;
        }

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
        if (pageP <= 0.001f && p >= 1f)
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
                DrawItems(og, menu, 1f, 1f, sx, sy, new PointF(-area.X, -area.Y));
            }

            g.DrawImageUnscaled(overlay, area.Location);
        }
        else
        {
            DrawGlassSkin(g, area, sx);
            var itemsFade = Math.Clamp(1f - pageP * 1.6f, 0f, 1f);
            if (itemsFade > 0.01f)
            {
                DrawItems(g, menu, p, itemsFade, sx, sy, PointF.Empty);
            }

            if (pages is { Current: { } page } && pageP > 0.3f)
            {
                DrawPage(g, page, pages, caretOn, (pageP - 0.3f) / 0.7f, sx, sy);
            }
        }

        g.ResetClip();
    }

    private void DrawPage(Graphics g, Page page, PageHost pages, bool caretOn, float appear, float sx, float sy) =>
        pageLayer.Draw(g, pages, page, ToneColor(accentTone), caretOn, appear * pages.ContentAlpha, sx, sy);

    private static Rectangle LerpRect(Rectangle a, Rectangle b, float t)
    {
        int L(int x, int y) => (int)MathF.Round(x + (y - x) * t);
        return new Rectangle(L(a.X, b.X), L(a.Y, b.Y), L(a.Width, b.Width), L(a.Height, b.Height));
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

    private void DrawItems(Graphics g, MenuModel menu, float progress, float fade, float sx, float sy, PointF offset)
    {
        using var format = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        for (var i = 0; i < menu.Items.Count; i++)
        {
            var a = menu.ItemAlpha(i, progress) * fade;
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

    private static GraphicsPath Rounded(RectangleF r, float radius) => Gfx.Rounded(r, radius);

    private static Color Mix(Color a, Color b, float t) => Gfx.Mix(a, b, t);

    private static float Smooth(float x) => Gfx.Smooth(x);
}
