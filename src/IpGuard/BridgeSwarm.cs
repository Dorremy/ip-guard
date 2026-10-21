namespace IpGuard;

public enum BridgeState
{
    Hidden,
    Opening,
    Open,
    Closing
}

public sealed class BridgeSwarm
{
    public const int Count = 360;
    public const float Reach = 140f;

    private const float OpenSeconds = 0.5f;
    private const float CloseSeconds = 0.8f;
    private const float MaxStep = 0.1f;

    private readonly Random random;
    private readonly float[] x = new float[Count];
    private readonly float[] y = new float[Count];
    private readonly float[] t = new float[Count];
    private readonly float[] speed = new float[Count];
    private readonly float[] size = new float[Count];
    private readonly float[] alpha = new float[Count];
    private readonly int[] direction = new int[Count];
    private readonly PointF[] start = new PointF[Count];
    private readonly PointF[] end = new PointF[Count];
    private readonly PointF[] control = new PointF[Count];
    private readonly float[] wobble = new float[Count];
    private readonly float[] phase = new float[Count];
    private readonly float[] brightness = new float[Count];
    private RectangleF source;
    private RectangleF target;

    public BridgeSwarm(int seed)
    {
        random = new Random(seed);
        for (var i = 0; i < Count; i++)
        {
            direction[i] = random.NextDouble() < 0.5 ? 1 : -1;
            speed[i] = 0.22f + (float)random.NextDouble() * 0.45f;
            size[i] = 0.8f + (float)random.NextDouble() * 1.2f;
            phase[i] = (float)random.NextDouble() * 6.2832f;
            brightness[i] = 0.55f + (float)random.NextDouble() * 0.45f;
            t[i] = (float)random.NextDouble();
        }
    }

    public BridgeState State { get; private set; } = BridgeState.Hidden;

    public float Intensity { get; private set; }

    public bool Finished => State == BridgeState.Hidden;

    public float Time { get; private set; }

    public int VisibleCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Count; i++)
            {
                if (alpha[i] > 0.01f)
                {
                    n++;
                }
            }

            return n;
        }
    }

    public float X(int i) => x[i];

    public float Y(int i) => y[i];

    public float Alpha(int i) => alpha[i];

    public float Size(int i) => size[i];

    public int Direction(int i) => direction[i];

    public float Progress(int i) => t[i];

    public void SetRects(RectangleF sourceRect, RectangleF targetRect)
    {
        source = Sane(sourceRect);
        target = Sane(targetRect);
    }

    public void Open()
    {
        State = State == BridgeState.Open ? BridgeState.Open : BridgeState.Opening;
    }

    public void Close()
    {
        if (State == BridgeState.Hidden)
        {
            return;
        }

        State = BridgeState.Closing;
    }

    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0 || State == BridgeState.Hidden)
        {
            return;
        }

        var h = Math.Min(dt, MaxStep);
        Time += h;
        switch (State)
        {
            case BridgeState.Opening:
                Intensity = Math.Min(1f, Intensity + h / OpenSeconds);
                if (Intensity >= 1f)
                {
                    State = BridgeState.Open;
                }

                break;
            case BridgeState.Closing:
                Intensity = Math.Max(0f, Intensity - h / CloseSeconds);
                if (Intensity <= 0f)
                {
                    State = BridgeState.Hidden;
                    for (var i = 0; i < Count; i++)
                    {
                        alpha[i] = 0f;
                    }

                    return;
                }

                break;
        }

        var closing = State == BridgeState.Closing;
        var active = (int)MathF.Ceiling(Count * Intensity);
        for (var i = 0; i < Count; i++)
        {
            if (i >= active && !closing)
            {
                alpha[i] = 0f;
                continue;
            }

            var pace = speed[i] * (closing ? 2.2f : 1f);
            t[i] += pace * h;
            if (t[i] >= 1f || t[i] < 0f || (start[i].X == 0 && start[i].Y == 0))
            {
                Resample(i, closing);
            }

            Place(i, closing);
        }
    }

    private void Resample(int i, bool closing)
    {
        t[i] = t[i] >= 1f ? t[i] - MathF.Floor(t[i]) : (t[i] < 0f ? 0f : t[i]);
        direction[i] = closing ? -1 : random.NextDouble() < 0.5 ? 1 : -1;
        var from = direction[i] > 0 ? source : target;
        var to = direction[i] > 0 ? target : source;
        var horizontal = MathF.Abs(Center(to).X - Center(from).X) >= MathF.Abs(Center(to).Y - Center(from).Y);
        start[i] = EdgePoint(from, to, horizontal);
        end[i] = EdgePoint(to, from, horizontal);
        var mid = new PointF((start[i].X + end[i].X) / 2, (start[i].Y + end[i].Y) / 2);
        var dx = end[i].X - start[i].X;
        var dy = end[i].Y - start[i].Y;
        var length = MathF.Sqrt(dx * dx + dy * dy) + 1f;
        var bend = ((float)random.NextDouble() * 2f - 1f) * MathF.Min(Reach * 0.8f, length * 0.3f);
        control[i] = new PointF(mid.X - dy / length * bend, mid.Y + dx / length * bend);
        wobble[i] = 4f + (float)random.NextDouble() * 12f;
    }

    private void Place(int i, bool closing)
    {
        var u = Math.Clamp(t[i], 0f, 1f);
        var a = start[i];
        var c = control[i];
        var b = end[i];
        var inv = 1f - u;
        var px = inv * inv * a.X + 2 * inv * u * c.X + u * u * b.X;
        var py = inv * inv * a.Y + 2 * inv * u * c.Y + u * u * b.Y;
        var tx = 2 * inv * (c.X - a.X) + 2 * u * (b.X - c.X);
        var ty = 2 * inv * (c.Y - a.Y) + 2 * u * (b.Y - c.Y);
        var len = MathF.Sqrt(tx * tx + ty * ty) + 1e-3f;
        var offset = MathF.Sin(u * 9f + phase[i] + Time * 1.5f) * wobble[i] * MathF.Sin(MathF.PI * u);
        x[i] = px - ty / len * offset;
        y[i] = py + tx / len * offset;
        var fade = MathF.Pow(MathF.Sin(MathF.PI * u), 1.2f);
        alpha[i] = Math.Clamp(fade * brightness[i] * (closing ? Intensity : 1f), 0f, 1f);
    }

    private PointF EdgePoint(RectangleF from, RectangleF toward, bool horizontal)
    {
        var inset = 10f;
        if (horizontal)
        {
            var right = Center(toward).X >= Center(from).X;
            var px = right ? from.Right : from.Left;
            var lo = from.Top + inset;
            var hi = Math.Max(lo, from.Bottom - inset);
            return new PointF(px, lo + (float)random.NextDouble() * (hi - lo));
        }

        var below = Center(toward).Y >= Center(from).Y;
        var py = below ? from.Bottom : from.Top;
        var left = from.Left + inset;
        var right2 = Math.Max(left, from.Right - inset);
        return new PointF(left + (float)random.NextDouble() * (right2 - left), py);
    }

    private static PointF Center(RectangleF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static RectangleF Sane(RectangleF r)
    {
        if (!float.IsFinite(r.X) || !float.IsFinite(r.Y) || !float.IsFinite(r.Width) || !float.IsFinite(r.Height))
        {
            return RectangleF.Empty;
        }

        return new RectangleF(r.X, r.Y, Math.Max(0f, r.Width), Math.Max(0f, r.Height));
    }
}
