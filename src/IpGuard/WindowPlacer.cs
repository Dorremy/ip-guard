namespace IpGuard;

public static class WindowPlacer
{
    private const int Margin = 8;

    public static Point Place(Size window, Rectangle panel, Rectangle work, int gap)
    {
        int x;
        var left = panel.Left - gap - window.Width;
        var right = panel.Right + gap;
        if (left >= work.Left + Margin)
        {
            x = left;
        }
        else if (right + window.Width <= work.Right - Margin)
        {
            x = right;
        }
        else
        {
            x = work.Left + (work.Width - window.Width) / 2;
        }

        var y = panel.Top + panel.Height / 2 - window.Height / 2;
        x = Math.Clamp(x, work.Left + Margin, Math.Max(work.Left + Margin, work.Right - Margin - window.Width));
        y = Math.Clamp(y, work.Top + Margin, Math.Max(work.Top + Margin, work.Bottom - Margin - window.Height));
        return new Point(x, y);
    }
}
