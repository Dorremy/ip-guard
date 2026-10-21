namespace IpGuard;

public static class TargetSampler
{
    public static PointF[] FromMask(bool[] mask, int width, int height, int count, Random rng)
    {
        var lit = new List<int>();
        for (var i = 0; i < mask.Length && i < width * height; i++)
        {
            if (mask[i])
            {
                lit.Add(i);
            }
        }

        if (lit.Count == 0 || count <= 0)
        {
            return Array.Empty<PointF>();
        }

        var points = new PointF[count];
        for (var k = 0; k < count; k++)
        {
            var cell = lit[rng.Next(lit.Count)];
            points[k] = new PointF(cell % width + (float)rng.NextDouble() * 0.999f, cell / width + (float)rng.NextDouble() * 0.999f);
        }

        return points;
    }

    public static PointF StreamPoint(float u, float time, float capsule, PointF center, float width, PointF noise)
    {
        var c = Math.Clamp(capsule, 0f, 1f);
        var along = (u + 0.3f * time) % 1f;
        if (along < 0)
        {
            along += 1f;
        }

        var span = width - 100f * c;
        var x = center.X + (along - 0.5f) * span;
        var y = center.Y + 8f * MathF.Sin(x * 0.05f - time * 3f) * (1f - c * 0.6f);
        return new PointF(x + noise.X * 15f * c, y + noise.Y * 12f * c);
    }
}
