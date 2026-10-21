namespace IpGuard;

public sealed class RowList
{
    private const float WheelStep = 54f;

    private readonly List<Row> rows = new();
    private readonly List<float> tops = new();
    private readonly List<float> heights = new();
    private float width = 300f;
    private float viewport = 200f;
    private int dragging = -1;
    private bool laidOut;

    public RowList(IEnumerable<Row>? initial = null)
    {
        if (initial is not null)
        {
            foreach (var row in initial)
            {
                Attach(row);
            }
        }

        Relayout();
    }

    public IReadOnlyList<Row> Rows => rows;

    public float Width => width;

    public float ViewportHeight => viewport;

    public float ContentHeight { get; private set; }

    public float ScrollY { get; private set; }

    public float MaxScroll => Math.Max(0f, ContentHeight - viewport);

    public int Hover { get; private set; } = -1;

    public RowPart HoverPart { get; private set; } = RowPart.None;

    public int Pressed { get; private set; } = -1;

    public RowPart PressedPart { get; private set; } = RowPart.None;

    public int Focus { get; private set; } = -1;

    public int Version { get; private set; }

    public void Touch() => Version++;

    public void Refresh() => Layout(width, viewport);

    public void Layout(float newWidth, float newViewport)
    {
        width = Math.Max(1f, newWidth);
        viewport = Math.Max(1f, newViewport);
        laidOut = true;
        Relayout();
    }

    public void Replace(IEnumerable<Row> next)
    {
        foreach (var row in rows)
        {
            row.Owner = null;
        }

        rows.Clear();
        foreach (var row in next)
        {
            Attach(row);
        }

        Hover = -1;
        HoverPart = RowPart.None;
        Pressed = -1;
        dragging = -1;
        Focus = -1;
        ScrollY = 0;
        Relayout();
    }

    public float TopOf(int index) => index >= 0 && index < tops.Count ? tops[index] : 0f;

    public float HeightOf(int index) => index >= 0 && index < heights.Count ? heights[index] : 0f;

    public void ScrollBy(float dy)
    {
        var next = Math.Clamp(ScrollY + dy, 0f, MaxScroll);
        if (next != ScrollY)
        {
            ScrollY = next;
            Touch();
        }
    }

    public void Wheel(float notches) => ScrollBy(-notches * WheelStep);

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= rows.Count)
        {
            return;
        }

        var top = tops[index];
        var bottom = top + heights[index];
        if (top < ScrollY)
        {
            ScrollBy(top - ScrollY);
        }
        else if (bottom > ScrollY + viewport)
        {
            ScrollBy(bottom - viewport - ScrollY);
        }
    }

    public (int Index, RowPart Part) HitTest(PointF point)
    {
        if (point.X < 0 || point.X > width || point.Y < 0 || point.Y > viewport)
        {
            return (-1, RowPart.None);
        }

        var y = point.Y + ScrollY;
        for (var i = 0; i < rows.Count; i++)
        {
            if (y >= tops[i] && y < tops[i] + heights[i])
            {
                var row = rows[i];
                return (i, row.Focusable ? row.HitPart(point.X, y - tops[i], width) : RowPart.None);
            }
        }

        return (-1, RowPart.None);
    }

    public bool Move(PointF point)
    {
        var (index, part) = HitTest(point);
        if (part == RowPart.None)
        {
            index = -1;
        }

        if (index == Hover && part == HoverPart)
        {
            return false;
        }

        Hover = index;
        HoverPart = part;
        Touch();
        return true;
    }

    public void Leave()
    {
        if (Hover != -1 || Pressed != -1)
        {
            Hover = -1;
            HoverPart = RowPart.None;
            Pressed = -1;
            PressedPart = RowPart.None;
            dragging = -1;
            Touch();
        }
    }

    public void Press(PointF point)
    {
        var (index, part) = HitTest(point);
        Pressed = part == RowPart.None ? -1 : index;
        PressedPart = Pressed < 0 ? RowPart.None : part;
        dragging = -1;
        if (Pressed >= 0 && rows[Pressed] is SliderRow slider && part == RowPart.Track)
        {
            dragging = Pressed;
            slider.SetFromFraction(slider.FractionAt(point.X, width));
        }

        if (Pressed >= 0 && rows[Pressed].Focusable)
        {
            Focus = Pressed;
        }

        Touch();
    }

    public void CancelPress()
    {
        Pressed = -1;
        PressedPart = RowPart.None;
        dragging = -1;
        Touch();
    }

    public void Drag(PointF point)
    {
        if (dragging >= 0 && dragging < rows.Count && rows[dragging] is SliderRow slider)
        {
            slider.SetFromFraction(slider.FractionAt(point.X, width));
        }
    }

    public bool Release(PointF point)
    {
        var pressed = Pressed;
        var pressedPart = PressedPart;
        var wasDragging = dragging >= 0;
        Pressed = -1;
        PressedPart = RowPart.None;
        dragging = -1;
        Touch();
        if (pressed < 0 || wasDragging)
        {
            return false;
        }

        var (index, part) = HitTest(point);
        if (index != pressed || part != pressedPart)
        {
            return false;
        }

        var fraction = rows[index] is SliderRow slider ? slider.FractionAt(point.X, width) : 0f;
        return rows[index].Click(part, fraction);
    }

    public void SetFocus(int index)
    {
        if (index >= 0 && index < rows.Count && rows[index].Focusable && index != Focus)
        {
            Focus = index;
            EnsureVisible(index);
            Touch();
        }
    }

    public void MoveFocus(int direction)
    {
        var count = rows.Count;
        if (count == 0)
        {
            return;
        }

        var step = direction >= 0 ? 1 : -1;
        var start = Focus < 0 ? (step > 0 ? -1 : count) : Focus;
        for (var n = 1; n <= count; n++)
        {
            var i = ((start + step * n) % count + count) % count;
            if (rows[i].Focusable)
            {
                Focus = i;
                EnsureVisible(i);
                Touch();
                return;
            }
        }

        if (Focus != -1)
        {
            Focus = -1;
            Touch();
        }
    }

    public bool Key(Keys key) => Focus >= 0 && Focus < rows.Count && rows[Focus].Key(key);

    private void Attach(Row row)
    {
        row.Owner = this;
        rows.Add(row);
    }

    private void Relayout()
    {
        tops.Clear();
        heights.Clear();
        var y = 0f;
        for (var i = 0; i < rows.Count; i++)
        {
            var h = rows[i].Height(width);
            tops.Add(y);
            heights.Add(h);
            y += h + (i < rows.Count - 1 ? RowMetrics.Gap : 0f);
        }

        ContentHeight = rows.Count == 0 ? 0f : y;
        if (laidOut || rows.Count > 0)
        {
            ScrollY = Math.Clamp(ScrollY, 0f, MaxScroll);
        }

        if (Focus >= rows.Count)
        {
            Focus = -1;
        }

        Touch();
    }
}
