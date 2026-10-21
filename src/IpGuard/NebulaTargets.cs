namespace IpGuard;

public static class NebulaTargets
{
    private const int Width = NebulaLayout.Width;
    private const int Height = NebulaLayout.Height;
    private const float StripY = NebulaLayout.StripY;
    private const float MaxTextWidth = 250f;
    private const float ShieldBox = 34f;
    private const float Gap = 10f;
    private const int StatusTextCount = 1400;
    private const int StatusShieldCount = IntroDirector.StatusCount - StatusTextCount;
    private const int CacheLimit = 24;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, PointF[]> Cache = new();

    public static IntroTargets Intro()
    {
        var name = GlyphMask.Text("IPGUARD", "Segoe UI Light", 52, FontStyle.Regular, 7, new PointF(Width / 2f, NebulaLayout.NameY), Width, Height);
        var logo = GlyphMask.Shield(new PointF(Width / 2f, NebulaLayout.LogoY), NebulaLayout.LogoRadius, 3.5f, ShieldGlyph.Check, Width, Height);
        return new IntroTargets(
            TargetSampler.FromMask(name, Width, Height, IntroDirector.NameCount, new Random(2)),
            TargetSampler.FromMask(logo, Width, Height, IntroDirector.LogoCount, new Random(3)));
    }

    public static PointF[] Status(PanelState state)
    {
        var label = PanelState.Fit(state.Label.Trim(), 30);
        lock (Sync)
        {
            if (Cache.TryGetValue(label, out var cached))
            {
                return cached;
            }
        }

        var points = Build(label);
        lock (Sync)
        {
            if (Cache.Count >= CacheLimit)
            {
                Cache.Clear();
            }

            Cache[label] = points;
        }

        return points;
    }

    private static PointF[] Build(string label)
    {
        var rng = new Random(label.GetHashCode() & 0x7fffffff);
        if (label.Length == 0)
        {
            return Fallback(rng);
        }

        var size = 28f;
        bool[] probe;
        var span = 0f;
        do
        {
            probe = GlyphMask.Text(label, "Segoe UI Semibold", size, FontStyle.Regular, 1.5f, new PointF(Width / 2f, StripY), Width, Height);
            span = Span(probe);
            size -= 1f;
        }
        while (span > MaxTextWidth && size >= 14f);

        if (span <= 0)
        {
            return Fallback(rng);
        }

        size += 1f;
        var groupWidth = ShieldBox + Gap + span;
        var left = Width / 2f - groupWidth / 2f;
        var text = GlyphMask.Text(label, "Segoe UI Semibold", size, FontStyle.Regular, 1.5f, new PointF(left + ShieldBox + Gap + span / 2f, StripY), Width, Height);
        var shield = GlyphMask.Shield(new PointF(left + ShieldBox / 2f, StripY), 16f, 2.6f, ShieldGlyph.Bang, Width, Height);
        return TargetSampler.FromMask(text, Width, Height, StatusTextCount, rng)
            .Concat(TargetSampler.FromMask(shield, Width, Height, StatusShieldCount, rng))
            .ToArray();
    }

    private static PointF[] Fallback(Random rng) =>
        Enumerable.Range(0, IntroDirector.StatusCount)
            .Select(i => TargetSampler.StreamPoint(i / (float)IntroDirector.StatusCount, 0f, 1f, new PointF(Width / 2f, StripY), 0.9f * Width, new PointF((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1)))
            .ToArray();

    private static float Span(bool[] mask)
    {
        var min = Width;
        var max = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (mask[y * Width + x])
                {
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }
        }

        return max < 0 ? 0 : max - min + 1;
    }
}
