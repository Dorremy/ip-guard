using System.Numerics;

namespace IpGuard;

public sealed class TrailLayer
{
    private const float Floor = 0.004f;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public float[] Data { get; private set; } = Array.Empty<float>();

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height && Data.Length == width * height)
        {
            return;
        }

        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Data = new float[Width * Height];
    }

    public void Clear() => Array.Clear(Data);

    public void Decay(float factor)
    {
        var k = float.IsFinite(factor) ? Math.Clamp(factor, 0f, 1f) : 0f;
        var data = Data;
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var scale = new Vector<float>(k);
            var floor = new Vector<float>(Floor);
            var step = Vector<float>.Count;
            for (; i <= data.Length - step; i += step)
            {
                var v = new Vector<float>(data, i) * scale;
                Vector.ConditionalSelect(Vector.GreaterThanOrEqual(v, floor), v, Vector<float>.Zero).CopyTo(data, i);
            }
        }

        for (; i < data.Length; i++)
        {
            var v = data[i] * k;
            data[i] = v < Floor ? 0f : v;
        }
    }

    public void Deposit(float x, float y, float weight)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(weight) || weight <= 0f)
        {
            return;
        }

        var fx = x - 0.5f;
        var fy = y - 0.5f;
        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        if (x0 < -1 || y0 < -1 || x0 >= Width || y0 >= Height)
        {
            return;
        }

        var ax = fx - x0;
        var ay = fy - y0;
        Add(x0, y0, weight * (1 - ax) * (1 - ay));
        Add(x0 + 1, y0, weight * ax * (1 - ay));
        Add(x0, y0 + 1, weight * (1 - ax) * ay);
        Add(x0 + 1, y0 + 1, weight * ax * ay);
    }

    private void Add(int x, int y, float value)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        Data[y * Width + x] += value;
    }
}
