namespace IpGuard;

public static class BridgeRenderer
{
    public static void Render(byte[] buffer, int width, int height, BridgeSwarm swarm, float originX, float originY, float scale, Color tint)
    {
        if (width < 1 || height < 1 || buffer.Length < width * height * 4)
        {
            return;
        }

        Array.Clear(buffer, 0, width * height * 4);
        for (var i = 0; i < BridgeSwarm.Count; i++)
        {
            var a = swarm.Alpha(i);
            if (a <= 0.01f)
            {
                continue;
            }

            var px = (swarm.X(i) - originX);
            var py = (swarm.Y(i) - originY);
            var radius = Math.Max(0.8f, swarm.Size(i) * scale);
            Splat(buffer, width, height, px, py, radius, a, tint);
        }
    }

    private static void Splat(byte[] buffer, int width, int height, float cx, float cy, float radius, float alpha, Color tint)
    {
        var reach = radius * 2.2f;
        var x0 = (int)MathF.Floor(cx - reach);
        var x1 = (int)MathF.Ceiling(cx + reach);
        var y0 = (int)MathF.Floor(cy - reach);
        var y1 = (int)MathF.Ceiling(cy + reach);
        for (var y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
        {
            for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
            {
                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - cy;
                var d2 = (dx * dx + dy * dy) / (radius * radius);
                var k = MathF.Exp(-d2 * 0.9f) * alpha;
                if (k < 0.01f)
                {
                    continue;
                }

                var idx = (y * width + x) * 4;
                var add = (int)(k * 255f);
                var na = Math.Min(255, buffer[idx + 3] + add);
                buffer[idx + 3] = (byte)na;
                buffer[idx] = (byte)Math.Min(na, buffer[idx] + (int)(add * tint.B / 255f));
                buffer[idx + 1] = (byte)Math.Min(na, buffer[idx + 1] + (int)(add * tint.G / 255f));
                buffer[idx + 2] = (byte)Math.Min(na, buffer[idx + 2] + (int)(add * tint.R / 255f));
            }
        }
    }
}
