namespace IpGuard;

public static class PageGeometry
{
    public const int HeaderHeight = 38;
    public const int FooterHeight = 46;
    public const int FooterPadding = 12;
    public const int FooterButtonWidth = 112;
    public const int FooterButtonHeight = 30;
    public const int FooterGap = 8;

    public static Rectangle Area => PageFrame.Panel.Area;

    public static Rectangle Back => PageFrame.Panel.Back;

    public static Rectangle Viewport(Page page) => PageFrame.Panel.Viewport(page);

    public static Rectangle FooterButton(int index, int count) => PageFrame.Panel.FooterButton(index, count);

    public static int FooterAt(Page page, PointF point) => PageFrame.Panel.FooterAt(page, point);

    public static bool IsBack(PointF point) => PageFrame.Panel.IsBack(point);

    public static PointF ToRows(Page page, PointF point) => PageFrame.Panel.ToRows(page, point);

    public static void Layout(Page page) => PageFrame.Panel.Layout(page);
}
