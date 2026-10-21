namespace IpGuard;

public sealed class PageFrame
{
    public PageFrame(Rectangle area)
    {
        Area = area;
        Back = new Rectangle(area.X + 8, area.Y + 5, 28, 28);
        Close = new Rectangle(area.Right - 8 - 28, area.Y + 5, 28, 28);
    }

    public static PageFrame Panel { get; } = new(new Rectangle(NebulaLayout.MenuLeft, 112, NebulaLayout.MenuRight - NebulaLayout.MenuLeft, NebulaLayout.MenuBottom - 112));

    public Rectangle Area { get; }

    public Rectangle Back { get; }

    public Rectangle Close { get; }

    public Rectangle Viewport(Page page)
    {
        var footer = page.Footer.Count > 0 ? PageGeometry.FooterHeight : 0;
        return new Rectangle(Area.X, Area.Y + PageGeometry.HeaderHeight, Area.Width, Area.Height - PageGeometry.HeaderHeight - footer);
    }

    public Rectangle FooterButton(int index, int count)
    {
        var right = Area.Right - PageGeometry.FooterPadding - (count - 1 - index) * (PageGeometry.FooterButtonWidth + PageGeometry.FooterGap);
        return new Rectangle(right - PageGeometry.FooterButtonWidth, Area.Bottom - (PageGeometry.FooterHeight + PageGeometry.FooterButtonHeight) / 2, PageGeometry.FooterButtonWidth, PageGeometry.FooterButtonHeight);
    }

    public int FooterAt(Page page, PointF point)
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

    public bool IsBack(PointF point) => Back.Contains((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));

    public bool IsClose(PointF point) => Close.Contains((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));

    public PointF ToRows(Page page, PointF point)
    {
        var vp = Viewport(page);
        return new PointF(point.X - vp.X, point.Y - vp.Y);
    }

    public void Layout(Page page)
    {
        var vp = Viewport(page);
        page.Rows.Layout(vp.Width, vp.Height);
    }
}
