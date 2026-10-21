namespace IpGuard;

public static class PageGeometry
{
    public const int HeaderHeight = 38;
    public const int FooterHeight = 46;
    public const int FooterPadding = 12;
    public const int FooterButtonWidth = 112;
    public const int FooterButtonHeight = 30;
    public const int FooterGap = 8;

    public static Rectangle Area { get; } = new(NebulaLayout.MenuLeft, 112, NebulaLayout.MenuRight - NebulaLayout.MenuLeft, NebulaLayout.MenuBottom - 112);

    public static Rectangle Back { get; } = new(Area.X + 8, Area.Y + 5, 28, 28);

    public static Rectangle Viewport(Page page)
    {
        var footer = page.Footer.Count > 0 ? FooterHeight : 0;
        return new Rectangle(Area.X, Area.Y + HeaderHeight, Area.Width, Area.Height - HeaderHeight - footer);
    }

    public static Rectangle FooterButton(int index, int count)
    {
        var right = Area.Right - FooterPadding - (count - 1 - index) * (FooterButtonWidth + FooterGap);
        return new Rectangle(right - FooterButtonWidth, Area.Bottom - (FooterHeight + FooterButtonHeight) / 2, FooterButtonWidth, FooterButtonHeight);
    }

    public static int FooterAt(Page page, PointF point)
    {
        var count = page.Footer.Count;
        for (var i = 0; i < count; i++)
        {
            if (FooterButton(i, count).Contains((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsBack(PointF point) => Back.Contains((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));

    public static PointF ToRows(Page page, PointF point)
    {
        var vp = Viewport(page);
        return new PointF(point.X - vp.X, point.Y - vp.Y);
    }

    public static void Layout(Page page)
    {
        var vp = Viewport(page);
        page.Rows.Layout(vp.Width, vp.Height);
    }
}
