namespace IpGuard;

public sealed record MenuItem(string Id, string Text);

public sealed class MenuModel
{
    public const int Left = NebulaLayout.MenuLeft;
    public const int Right = NebulaLayout.MenuRight;
    public const int Bottom = NebulaLayout.MenuBottom;
    public const int ItemHeight = 34;
    public const int Gap = 7;
    public const int Inset = 7;

    public MenuModel(IReadOnlyList<MenuItem> items)
    {
        Items = items;
    }

    public IReadOnlyList<MenuItem> Items { get; }

    public int Hover { get; private set; } = -1;

    public int Pressed { get; private set; } = -1;

    public int Focus { get; private set; } = -1;

    public static MenuModel Default() => new(new[]
    {
        new MenuItem("check", "Проверить сейчас"),
        new MenuItem("history", "История"),
        new MenuItem("references", "Эталоны"),
        new MenuItem("settings", "Настройки"),
        new MenuItem("journal", "Открыть журнал"),
        new MenuItem("folder", "Папка с данными")
    });

    private int FullHeight => Items.Count * ItemHeight + (Items.Count - 1) * Gap + 2 * Inset;

    public Rectangle Area(float progress)
    {
        if (progress <= 0f)
        {
            return Rectangle.Empty;
        }

        var a = EaseOut(progress / 0.35f);
        var b = EaseOut((progress - 0.2f) / 0.7f);
        var fullWidth = Right - Left;
        var width = (int)MathF.Round(Inset + (fullWidth - Inset) * a);
        var height = (int)MathF.Round(Inset + (FullHeight - Inset) * b);
        var centerX = (Left + Right) / 2f;
        return new Rectangle((int)MathF.Round(centerX - width / 2f), Bottom - height, width, height);
    }

    public Rectangle ItemBounds(int index)
    {
        var top = Bottom - FullHeight + Inset + index * (ItemHeight + Gap);
        return new Rectangle(Left + Inset, top, Right - Left - 2 * Inset, ItemHeight);
    }

    public float ItemAlpha(int index, float progress) => EaseOut((progress - 0.35f - 0.05f * (Items.Count - 1 - index)) / 0.25f);

    public int HitTest(PointF point)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (ItemBounds(i).Contains((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)))
            {
                return i;
            }
        }

        return -1;
    }

    public bool Move(PointF point)
    {
        var hit = HitTest(point);
        if (hit == Hover)
        {
            return false;
        }

        Hover = hit;
        return true;
    }

    public void Leave()
    {
        Hover = -1;
        Pressed = -1;
    }

    public void Press(PointF point) => Pressed = HitTest(point);

    public string? Release(PointF point)
    {
        var pressed = Pressed;
        Pressed = -1;
        return pressed >= 0 && HitTest(point) == pressed ? Items[pressed].Id : null;
    }

    public string? Key(Keys key)
    {
        var count = Items.Count;
        if (count == 0)
        {
            return null;
        }

        switch (key)
        {
            case Keys.Down:
            case Keys.Tab:
                Focus = Focus < 0 ? 0 : (Focus + 1) % count;
                break;
            case Keys.Up:
                Focus = Focus < 0 ? count - 1 : (Focus - 1 + count) % count;
                break;
            case Keys.Home:
                Focus = 0;
                break;
            case Keys.End:
                Focus = count - 1;
                break;
            case Keys.Enter:
            case Keys.Space:
                return Focus >= 0 ? Items[Focus].Id : null;
        }

        return null;
    }

    private static float EaseOut(float x)
    {
        var t = Math.Clamp(x, 0f, 1f);
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }
}
