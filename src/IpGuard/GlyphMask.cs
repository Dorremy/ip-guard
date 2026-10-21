using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IpGuard;

public enum ShieldGlyph
{
    Check,
    Bang
}

public static class GlyphMask
{
    public static bool[] Text(string text, string fontName, float sizePx, FontStyle style, float tracking, PointF center, int width, int height)
    {
        using var font = new Font(fontName, sizePx, style, GraphicsUnit.Pixel);
        var first = Render(text, font, tracking, width, height, new PointF(width / 2f, height / 2f), out var box);
        if (box.IsEmpty)
        {
            return first;
        }

        var dx = center.X - (box.Left + box.Right) / 2f;
        var dy = center.Y - (box.Top + box.Bottom) / 2f;
        return Render(text, font, tracking, width, height, new PointF(width / 2f + dx, height / 2f + dy), out _);
    }

    public static bool[] Shield(PointF center, float radius, float lineWidth, ShieldGlyph glyph, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.White, lineWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            var outline = new[] { (0f, -1f), (.86f, -.62f), (.86f, .1f), (.6f, .6f), (0f, 1f), (-.6f, .6f), (-.86f, .1f), (-.86f, -.62f), (0f, -1f) };
            g.DrawLines(pen, outline.Select(p => new PointF(center.X + p.Item1 * radius, center.Y + p.Item2 * radius * 1.05f)).ToArray());
            if (glyph == ShieldGlyph.Check)
            {
                var tick = new[] { (-.38f, .02f), (-.1f, .32f), (.42f, -.3f) };
                g.DrawLines(pen, tick.Select(p => new PointF(center.X + p.Item1 * radius, center.Y + p.Item2 * radius)).ToArray());
            }
            else
            {
                g.DrawLine(pen, center.X, center.Y - .45f * radius, center.X, center.Y + .1f * radius);
                using var brush = new SolidBrush(Color.White);
                var dot = lineWidth * 0.7f;
                g.FillEllipse(brush, center.X - dot, center.Y + .38f * radius - dot, dot * 2, dot * 2);
            }
        }

        return Read(bitmap, out _);
    }

    private static bool[] Render(string text, Font font, float tracking, int width, int height, PointF center, out Rectangle box)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(Color.White);
            using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
            var widths = text.Select(c => c == ' ' ? font.Size * 0.28f : g.MeasureString(c.ToString(), font, int.MaxValue, format).Width).ToArray();
            var total = widths.Sum() + tracking * Math.Max(0, text.Length - 1);
            var x = center.X - total / 2f;
            var y = center.Y - font.Height / 2f;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    g.DrawString(text[i].ToString(), font, brush, x, y, format);
                }

                x += widths[i] + tracking;
            }
        }

        return Read(bitmap, out box);
    }

    private static bool[] Read(Bitmap bitmap, out Rectangle box)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[Math.Abs(data.Stride) * h];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bitmap.UnlockBits(data);
        var mask = new bool[w * h];
        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (bytes[y * data.Stride + x * 4 + 3] > 128)
                {
                    mask[y * w + x] = true;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        box = maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        return mask;
    }
}
