namespace IpGuard;

public static class Curtain
{
    private static readonly (float X, float Y, float R, float A)[] Blobs =
    {
        (0.25f, 0.20f, 0.35f, 0.10f),
        (0.80f, 0.55f, 0.40f, 0.08f),
        (0.40f, 0.90f, 0.45f, 0.09f),
        (0.10f, 0.70f, 0.30f, 0.07f)
    };

    private enum Side
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private sealed record Wisp(Side Side, float Base, float Wander1, float Wander2, float Along, float Depth, float Amp,
        float RateA, float RateB, float RateC, float PhaseA, float PhaseB, float PhaseC, float RateP, float RateQ, float PhaseP, float PhaseQ);

    private static readonly Wisp[] Wisps = MakeWisps();

    private static Wisp[] MakeWisps()
    {
        var random = new Random(20261023);
        float Range(float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);
        var sides = new[] { Side.Left, Side.Left, Side.Left, Side.Left, Side.Right, Side.Right, Side.Right, Side.Right, Side.Top, Side.Bottom };
        return sides.Select(side => new Wisp(
            side, Range(0.08f, 0.92f), Range(0.05f, 0.14f), Range(0.03f, 0.08f), Range(0.10f, 0.22f), Range(0.055f, 0.11f), Range(0.55f, 1.25f),
            Range(0.083f, 0.31f), Range(0.047f, 0.19f), Range(0.011f, 0.071f), Range(0, 6.2832f), Range(0, 6.2832f), Range(0, 6.2832f),
            Range(0.05f, 0.21f), Range(0.09f, 0.27f), Range(0, 6.2832f), Range(0, 6.2832f))).ToArray();
    }

    private static float Pulse(Wisp w, float time)
    {
        var s = 0.5f * MathF.Sin(time * w.RateA + w.PhaseA) + 0.3f * MathF.Sin(time * w.RateB + w.PhaseB) + 0.2f * MathF.Sin(time * w.RateC + w.PhaseC);
        var v = Math.Clamp((s + 0.25f) / 0.85f, 0f, 1f);
        return v * v * (3f - 2f * v);
    }

    private static float WispGlow(float u, float v, float time, float[] pulse, float[] anchor)
    {
        var sum = 0f;
        for (var i = 0; i < Wisps.Length; i++)
        {
            var w = Wisps[i];
            var strength = pulse[i];
            if (strength < 0.01f)
            {
                continue;
            }

            float along;
            float perp;
            switch (w.Side)
            {
                case Side.Left:
                    along = (v - anchor[i]) / w.Along;
                    perp = u / w.Depth;
                    break;
                case Side.Right:
                    along = (v - anchor[i]) / w.Along;
                    perp = (1 - u) / w.Depth;
                    break;
                case Side.Top:
                    along = (u - anchor[i]) / (w.Along * 1.4f);
                    perp = v / (w.Depth * 1.3f);
                    break;
                default:
                    along = (u - anchor[i]) / (w.Along * 1.4f);
                    perp = (1 - v) / (w.Depth * 1.3f);
                    break;
            }

            if (perp > 3f || MathF.Abs(along) > 3f)
            {
                continue;
            }

            sum += w.Amp * strength * MathF.Exp(-(along * along + perp * perp));
        }

        return 1.2f * MathF.Tanh(sum / 1.2f);
    }

    public static void Fill(Span<uint> pixels, int width, int height, float time, float dim, float warm = 0f, float glow = 1f)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return;
        }

        if (!float.IsFinite(time))
        {
            time = 0;
        }

        dim = float.IsFinite(dim) ? Math.Clamp(dim, 0f, 1f) : 1f;
        warm = float.IsFinite(warm) ? Math.Clamp(warm, 0f, 1f) : 0f;
        glow = float.IsFinite(glow) ? Math.Clamp(glow, 0f, 2f) : 1f;
        var pulse = new float[Wisps.Length];
        var anchor = new float[Wisps.Length];
        for (var i = 0; i < Wisps.Length; i++)
        {
            var w = Wisps[i];
            pulse[i] = Pulse(w, time);
            anchor[i] = w.Base + w.Wander1 * MathF.Sin(time * w.RateP + w.PhaseP) + w.Wander2 * MathF.Sin(time * w.RateQ + w.PhaseQ);
        }

        var centers = new (float X, float Y)[Blobs.Length];
        for (var i = 0; i < Blobs.Length; i++)
        {
            centers[i] = (Blobs[i].X + 0.18f * MathF.Sin(time * 0.35f * (1 + i * 0.17f) + i), Blobs[i].Y + 0.15f * MathF.Cos(time * 0.3f * (1 + i * 0.13f) + i * 2));
        }

        for (var y = 0; y < height; y++)
        {
            var v = height == 1 ? 0f : y / (float)(height - 1);
            for (var x = 0; x < width; x++)
            {
                var u = width == 1 ? 0f : x / (float)(width - 1);
                var field = 0f;
                for (var i = 0; i < Blobs.Length; i++)
                {
                    var dx = u - centers[i].X;
                    var dy = v - centers[i].Y;
                    field += Blobs[i].A * MathF.Exp(-(dx * dx + dy * dy * 0.55f) / (Blobs[i].R * Blobs[i].R));
                }

                var g = Math.Clamp(0.03f + field + 0.19f * glow * WispGlow(u, v, time, pulse, anchor), 0f, 1f) * 255f * dim;
                var r = (byte)Math.Min(255f, g * (0.92f + 0.24f * warm));
                var gr = (byte)Math.Min(255f, g * (0.97f - 0.02f * warm));
                var b = (byte)Math.Min(255f, g * (1.06f - 0.30f * warm));
                pixels[y * width + x] = 0xFF000000u | (uint)(r << 16) | (uint)(gr << 8) | b;
            }
        }
    }
}
