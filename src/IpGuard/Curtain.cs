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

    public static void Fill(Span<uint> pixels, int width, int height, float time, float dim)
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

                var edge = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v * 0.6f, (1 - v) * 0.6f));
                var patches = Math.Clamp(MathF.Sin(u * 9 + time * 0.5f) * MathF.Sin(v * 7 - time * 0.4f) + 0.25f, 0f, 1f);
                var g = Math.Clamp(0.03f + field + 0.22f * MathF.Exp(-edge / 0.08f) * patches, 0f, 1f) * 255f * dim;
                var r = (byte)Math.Min(255f, g * 0.92f);
                var gr = (byte)Math.Min(255f, g * 0.97f);
                var b = (byte)Math.Min(255f, g * 1.06f);
                pixels[y * width + x] = 0xFF000000u | (uint)(r << 16) | (uint)(gr << 8) | b;
            }
        }
    }
}
