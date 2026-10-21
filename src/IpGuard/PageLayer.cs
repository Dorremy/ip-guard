using System.Drawing.Imaging;

namespace IpGuard;

public sealed class PageLayer : IDisposable
{
    private PageFonts? fonts;
    private Bitmap? cache;
    private (Page? Page, int Rows, int Host, int Argb, bool Caret, int Width, int Height) key;

    public void Draw(Graphics g, PageHost pages, Page page, Color accent, bool caretOn, float alpha, float sx, float sy)
    {
        if (fonts is null || Math.Abs(fonts.Scale - sx) > 0.001f)
        {
            fonts?.Dispose();
            fonts = new PageFonts(sx);
            cache?.Dispose();
            cache = null;
        }

        var area = pages.Frame.Area;
        var width = Math.Max(1, (int)(area.Width * sx));
        var height = Math.Max(1, (int)(area.Height * sy));
        var rows = page.Rows;
        var focusText = rows.Focus >= 0 && rows.Focus < rows.Rows.Count && rows.Rows[rows.Focus] is TextRow;
        var next = (page, rows.Version, pages.Version, accent.ToArgb(), caretOn && focusText, width, height);
        if (cache is null || next != key)
        {
            cache?.Dispose();
            cache = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            key = next;
            using var pg = Graphics.FromImage(cache);
            PageDrawing.Render(pg, page, pages, accent, caretOn && focusText, sx, sy, fonts);
        }

        Gfx.DrawFaded(g, cache, new Rectangle((int)(area.X * sx), (int)(area.Y * sy), width, height), Math.Clamp(alpha, 0f, 1f));
    }

    public void Dispose()
    {
        fonts?.Dispose();
        cache?.Dispose();
    }
}
