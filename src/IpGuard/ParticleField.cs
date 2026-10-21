namespace IpGuard;

public enum Drive : byte
{
    Flow,
    Spring,
    Roam
}

public sealed class ParticleField
{
    private const float MaxStep = 0.05f;
    private const float SubStep = 1f / 120f;
    private const float Column = 30f;

    private readonly Random random;
    private readonly float unit;
    private readonly float[] vx;
    private readonly float[] vy;
    private readonly float[] tx;
    private readonly float[] ty;
    private readonly float[] hx;
    private readonly float[] hy;
    private readonly float[] stiffness;
    private readonly float[] speed;
    private readonly float[] phase;
    private readonly float[] activate;

    public ParticleField(int count, float width, float height, int seed)
    {
        random = new Random(seed);
        Count = count;
        Width = width;
        Height = height;
        unit = width / 600f;
        X = new float[count];
        Y = new float[count];
        Brightness = new float[count];
        Modes = new Drive[count];
        vx = new float[count];
        vy = new float[count];
        tx = new float[count];
        ty = new float[count];
        hx = new float[count];
        hy = new float[count];
        stiffness = new float[count];
        speed = new float[count];
        phase = new float[count];
        activate = new float[count];
        for (var i = 0; i < count; i++)
        {
            X[i] = Range(0.03f * width, 0.97f * width);
            Y[i] = Range(0.03f * height, 0.97f * height);
            vx[i] = Gauss() * 12 * unit;
            vy[i] = Gauss() * 12 * unit;
            tx[i] = X[i];
            ty[i] = Y[i];
            Brightness[i] = Range(0.55f, 1f);
            speed[i] = Range(0.6f, 1.4f);
            phase[i] = Range(0, 6.2832f);
            stiffness[i] = Range(35, 80);
            NewHome(i);
        }
    }

    public int Count { get; }

    public float Width { get; }

    public float Height { get; }

    public float Time { get; private set; }

    public float[] X { get; }

    public float[] Y { get; }

    public float[] Brightness { get; }

    public Drive[] Modes { get; }

    public float Vx(int i) => vx[i];

    public float Vy(int i) => vy[i];

    public PointF TargetOf(int i) => new(tx[i], ty[i]);

    public bool Driven(int i) => Modes[i] == Drive.Spring && Time >= activate[i];

    public void Assign(ReadOnlySpan<int> indices, ReadOnlySpan<PointF> targets, float maxDelay)
    {
        if (indices.Length != targets.Length)
        {
            throw new ArgumentException("Число частиц и целей должно совпадать");
        }

        var n = indices.Length;
        if (n == 0)
        {
            return;
        }

        var particles = indices.ToArray();
        var particleKeys = particles.Select(i => (Math.Floor(X[i] / Column), (double)Y[i])).ToArray();
        Array.Sort(particleKeys, particles);
        var points = targets.ToArray();
        var pointKeys = points.Select(p => (Math.Floor(p.X / Column), (double)p.Y)).ToArray();
        Array.Sort(pointKeys, points);
        for (var k = 0; k < n; k++)
        {
            var i = particles[k];
            tx[i] = points[k].X;
            ty[i] = points[k].Y;
            Modes[i] = Drive.Spring;
            activate[i] = Time + Range(0, Math.Max(0, maxDelay));
            stiffness[i] = Range(35, 80);
        }
    }

    public void Follow(ReadOnlySpan<int> indices, ReadOnlySpan<PointF> targets)
    {
        if (indices.Length != targets.Length)
        {
            throw new ArgumentException("Число частиц и целей должно совпадать");
        }

        for (var k = 0; k < indices.Length; k++)
        {
            var i = indices[k];
            tx[i] = targets[k].X;
            ty[i] = targets[k].Y;
            if (Modes[i] != Drive.Spring)
            {
                Modes[i] = Drive.Spring;
                activate[i] = Time;
            }
            else
            {
                activate[i] = Math.Min(activate[i], Time);
            }
        }
    }

    public void Scatter()
    {
        for (var i = 0; i < Count; i++)
        {
            X[i] = Range(0.03f * Width, 0.97f * Width);
            Y[i] = Range(0.03f * Height, 0.97f * Height);
            vx[i] = Gauss() * 12 * unit;
            vy[i] = Gauss() * 12 * unit;
            Modes[i] = Drive.Flow;
        }
    }

    public void Snap(ReadOnlySpan<int> indices)
    {
        foreach (var i in indices)
        {
            X[i] = tx[i];
            Y[i] = ty[i];
            vx[i] = 0;
            vy[i] = 0;
            Modes[i] = Drive.Spring;
            activate[i] = Time;
        }
    }

    public void SetStiffness(ReadOnlySpan<int> indices, float value)
    {
        foreach (var i in indices)
        {
            stiffness[i] = value;
        }
    }

    public void Release(ReadOnlySpan<int> indices, float burst)
    {
        var cx = Width / 2;
        var cy = Height / 2;
        foreach (var i in indices)
        {
            Modes[i] = Drive.Flow;
            var dx = X[i] - cx;
            var dy = Y[i] - cy;
            var len = MathF.Sqrt(dx * dx + dy * dy) + 1;
            vx[i] += dx / len * burst * unit + Gauss() * 25 * unit;
            vy[i] += dy / len * burst * unit + Gauss() * 25 * unit;
        }
    }

    public void Roam(ReadOnlySpan<int> indices)
    {
        foreach (var i in indices)
        {
            Modes[i] = Drive.Roam;
            NewHome(i);
        }
    }

    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0)
        {
            if (float.IsPositiveInfinity(dt))
            {
                dt = MaxStep;
            }
            else
            {
                return;
            }
        }

        var remaining = Math.Min(dt, MaxStep);
        while (remaining > 1e-6f)
        {
            var h = Math.Min(remaining, SubStep);
            Integrate(h);
            remaining -= h;
        }
    }

    private void Integrate(float dt)
    {
        var t = Time;
        for (var i = 0; i < Count; i++)
        {
            var px = X[i];
            var py = Y[i];
            var ax = 0f;
            var ay = 0f;
            switch (Modes[i])
            {
                case Drive.Spring when t >= activate[i]:
                {
                    var k = stiffness[i];
                    var c = 1.7f * MathF.Sqrt(k);
                    ax = k * (tx[i] - px) - c * vx[i];
                    ay = k * (ty[i] - py) - c * vy[i];
                    break;
                }

                case Drive.Roam:
                {
                    var gx = hx[i] + 70 * unit * MathF.Sin(t * 0.4f + phase[i] * 1.7f);
                    var gy = hy[i] + 60 * unit * MathF.Cos(t * 0.33f + phase[i] * 2.3f);
                    ax = 3f * (gx - px) - 1.6f * vx[i];
                    ay = 3f * (gy - py) - 1.6f * vy[i];
                    break;
                }

                default:
                {
                    var ux = px / unit;
                    var uy = py / unit;
                    var angle = t * 0.1f + phase[i] * 0.05f;
                    var fx = 14 * MathF.Cos(angle) + 22 * MathF.Sin(uy * 0.011f + t * 0.55f + phase[i]) + 12 * MathF.Sin(uy * 0.019f - t * 0.37f + phase[i] * 1.7f);
                    var fy = 14 * MathF.Sin(angle) + 20 * MathF.Cos(ux * 0.012f - t * 0.5f + phase[i]) + 10 * MathF.Cos(ux * 0.021f + t * 0.31f + phase[i] * 2.1f);
                    ax = (fx * speed[i] * unit - vx[i]) * 1.6f;
                    ay = (fy * speed[i] * unit - vy[i]) * 1.6f;
                    break;
                }
            }

            var wrap = Modes[i] == Drive.Flow;
            if (!wrap)
            {
                var margin = 40 * unit;
                ax += (MathF.Max(margin - px, 0) - MathF.Max(px - (Width - margin), 0)) * 3f;
                ay += (MathF.Max(margin - py, 0) - MathF.Max(py - (Height - margin), 0)) * 3f;
            }

            vx[i] += ax * dt;
            vy[i] += ay * dt;
            var nx = px + vx[i] * dt;
            var ny = py + vy[i] * dt;
            if (!float.IsFinite(nx) || !float.IsFinite(ny))
            {
                nx = Range(0.1f * Width, 0.9f * Width);
                ny = Range(0.1f * Height, 0.9f * Height);
                vx[i] = 0;
                vy[i] = 0;
            }

            X[i] = wrap ? Wrap(nx, Width) : Math.Clamp(nx, 0, Width);
            Y[i] = wrap ? Wrap(ny, Height) : Math.Clamp(ny, 0, Height);
        }

        Time += dt;
    }

    private static float Wrap(float value, float size)
    {
        var v = value % size;
        return v < 0 ? v + size : v;
    }

    private void NewHome(int i)
    {
        hx[i] = Range(0.12f * Width, 0.88f * Width);
        hy[i] = Range(0.38f * Height, 0.92f * Height);
    }

    private float Range(float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);

    private float Gauss()
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
