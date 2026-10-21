using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace IpGuard;

public sealed class WindowRenderer : IDisposable
{
    private const float LinkDistance = 70f;
    private static readonly Color Pale = Color.FromArgb(205, 216, 235);

    private readonly Size logical;
    private readonly CurtainCache curtain = new(105, 78);
    private readonly SwarmPainter painter = new();
    private readonly PageLayer layer = new();
    private Bitmap? glass;
    private GraphicsPath? glassPath;
    private Rectangle glassArea;

    public WindowRenderer(Size logical)
    {
        this.logical = logical;
    }

    public void Render(Bitmap target, AmbientScene scene, PageHost pages, bool caretOn, float open)
    {
        var w = target.Width;
        var h = target.Height;
        if (w < 8 || h < 8)
        {
            return;
        }

        var sx = w / (float)logical.Width;
        var sy = h / (float)logical.Height;
        using (var g = Graphics.FromImage(target))
        {
            curtain.Draw(g, w, h, scene.Time, scene.Dim);
        }

        Splat(target, scene, sx, sy);
        using (var g = Graphics.FromImage(target))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            DrawTile(g, target, scene, pages, caretOn, open, sx, sy);
            using var border = new Pen(Color.FromArgb(26, 255, 255, 255), 1f);
            g.DrawRectangle(border, 0.5f, 0.5f, w - 1.5f, h - 1.5f);
        }
    }

    public void Dispose()
    {
        curtain.Dispose();
        layer.Dispose();
        glass?.Dispose();
        glassPath?.Dispose();
    }

    private void Splat(Bitmap target, AmbientScene scene, float sx, float sy)
    {
        painter.Begin(target, sx);
        var f = scene.Field;
        var life = scene.Appear;
        var tint = Gfx.Mix(Pale, NebulaRenderer.ToneColor(scene.Tone), 0.22f);
        painter.Splat(f, sx, sy, scene.Time, i =>
        {
            var b = f.Brightness[i] * life * 0.85f;
            var edge = MathF.Min(MathF.Min(f.X[i], f.Width - f.X[i]), MathF.Min(f.Y[i], f.Height - f.Y[i]));
            if (edge < 14f)
            {
                b *= Gfx.Smooth(edge / 14f);
            }

            return new Dot(b, tint, 0.3f, f.Modes[i] == Drive.Roam ? 0.3f : 0f);
        });
        var roam = scene.Roamers;
        var level = scene.LinkLevel;
        if (level > 0.01f)
        {
            var limit = LinkDistance * sx;
            var thickness = sx >= 1.5f ? 2 : 1;
            for (var a = 0; a < roam.Count; a++)
            {
                var ax = f.X[roam[a]] * sx;
                var ay = f.Y[roam[a]] * sy;
                for (var c = a + 1; c < roam.Count; c++)
                {
                    var dx = f.X[roam[c]] * sx - ax;
                    var dy = f.Y[roam[c]] * sy - ay;
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < limit)
                    {
                        var alpha = (int)(110f * (1f - dist / limit) * level);
                        if (alpha >= 4)
                        {
                            painter.Line(ax, ay, dx, dy, alpha, thickness);
                        }
                    }
                }
            }
        }

        painter.End();
    }

    private void DrawTile(Graphics g, Bitmap target, AmbientScene scene, PageHost pages, bool caretOn, float open, float sx, float sy)
    {
        var frame = pages.Frame.Area;
        var area = new Rectangle((int)(frame.X * sx), (int)(frame.Y * sy), Math.Max(1, (int)(frame.Width * sx)), Math.Max(1, (int)(frame.Height * sy)));
        if (glassPath is null || glassArea != area)
        {
            glassPath?.Dispose();
            glassPath = Gfx.Rounded(area, 14f * sx);
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

        var tileAlpha = Math.Clamp(open, 0f, 1f);
        g.SetClip(glassPath);
        Gfx.DrawFaded(g, glass, area, tileAlpha);
        using (var shade = new SolidBrush(Color.FromArgb((int)(70 * tileAlpha), 0, 0, 0)))
        {
            g.FillPath(shade, glassPath);
        }

        using (var fill = new SolidBrush(Color.FromArgb((int)(14 * tileAlpha), 255, 255, 255)))
        {
            g.FillPath(fill, glassPath);
        }

        if (pages.Current is { } page)
        {
            layer.Draw(g, pages, page, NebulaRenderer.ToneColor(scene.Tone), caretOn, tileAlpha * pages.ContentAlpha, sx, sy);
        }

        g.ResetClip();
        using var edge = new Pen(Color.FromArgb((int)(44 * tileAlpha), 255, 255, 255), Math.Max(1f, sx));
        g.DrawPath(edge, glassPath);
    }
}
