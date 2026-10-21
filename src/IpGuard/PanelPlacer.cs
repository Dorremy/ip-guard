namespace IpGuard;

public sealed record PanelPlacement(Point Location, Point SlideFrom);

public static class PanelPlacer
{
    public const int Margin = 8;
    public const int Slide = 24;
    public const float MinScale = 0.75f;

    public static PanelPlacement Place(Size panel, Rectangle bounds, Rectangle work, Point cursor)
    {
        int x;
        int y;
        Point offset;
        if (work.Top > bounds.Top)
        {
            x = cursor.X - panel.Width / 2;
            y = work.Top + Margin;
            offset = new Point(0, -Slide);
        }
        else if (work.Left > bounds.Left)
        {
            x = work.Left + Margin;
            y = cursor.Y - panel.Height / 2;
            offset = new Point(-Slide, 0);
        }
        else if (work.Right < bounds.Right)
        {
            x = work.Right - Margin - panel.Width;
            y = cursor.Y - panel.Height / 2;
            offset = new Point(Slide, 0);
        }
        else
        {
            x = cursor.X - panel.Width / 2;
            y = work.Bottom - Margin - panel.Height;
            offset = new Point(0, Slide);
        }

        x = Math.Clamp(x, work.Left + Margin, Math.Max(work.Left + Margin, work.Right - Margin - panel.Width));
        y = Math.Clamp(y, work.Top + Margin, Math.Max(work.Top + Margin, work.Bottom - Margin - panel.Height));
        return new PanelPlacement(new Point(x, y), new Point(x + offset.X, y + offset.Y));
    }

    public static float ScaleFor(float dpi, Rectangle work, Size logical)
    {
        var scale = Math.Max(MinScale, dpi / 96f);
        var room = new SizeF(work.Width - 2 * Margin, work.Height - 2 * Margin);
        var fit = Math.Min(room.Width / logical.Width, room.Height / logical.Height);
        return Math.Max(MinScale, Math.Min(scale, fit));
    }

    public static Size Physical(Size logical, float scale) =>
        new((int)Math.Round(logical.Width * scale), (int)Math.Round(logical.Height * scale));
}
