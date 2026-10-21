namespace IpGuard;

public static class Easing
{
    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    public static double Linear(double t) => Clamp01(t);

    public static double OutQuad(double t)
    {
        t = Clamp01(t);
        return 1 - (1 - t) * (1 - t);
    }

    public static double OutCubic(double t)
    {
        t = Clamp01(t);
        return 1 - Math.Pow(1 - t, 3);
    }

    public static double InCubic(double t)
    {
        t = Clamp01(t);
        return t * t * t;
    }

    public static double InOutCubic(double t)
    {
        t = Clamp01(t);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    public static double OutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        t = Clamp01(t);
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    public static double OutElastic(double t)
    {
        t = Clamp01(t);
        if (t is 0 or 1)
        {
            return t;
        }

        const double c4 = 2 * Math.PI / 3;
        return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    }

    public static double OutBounce(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        t = Clamp01(t);
        if (t < 1 / d1)
        {
            return n1 * t * t;
        }

        if (t < 2 / d1)
        {
            t -= 1.5 / d1;
            return n1 * t * t + 0.75;
        }

        if (t < 2.5 / d1)
        {
            t -= 2.25 / d1;
            return n1 * t * t + 0.9375;
        }

        t -= 2.625 / d1;
        return n1 * t * t + 0.984375;
    }
}

public sealed class Tween
{
    private Func<double, double> ease = Easing.Linear;
    private double elapsed;
    private double delay;

    public double From { get; private set; }

    public double To { get; private set; }

    public double Duration { get; private set; }

    public double Value { get; private set; }

    public bool Finished { get; private set; } = true;

    public Tween(double value = 0)
    {
        From = To = Value = value;
    }

    public void Start(double from, double to, double seconds, Func<double, double>? easing = null, double delaySeconds = 0)
    {
        From = from;
        To = to;
        Duration = Math.Max(0, seconds);
        ease = easing ?? Easing.Linear;
        delay = Math.Max(0, delaySeconds);
        elapsed = 0;
        Value = from;
        Finished = false;
        if (Duration == 0 && delay == 0)
        {
            Value = to;
            Finished = true;
        }
    }

    public void StartFromCurrent(double to, double seconds, Func<double, double>? easing = null) =>
        Start(Value, to, seconds, easing);

    public double Update(double deltaSeconds)
    {
        if (Finished)
        {
            return Value;
        }

        var step = Math.Max(0, deltaSeconds);
        if (delay > 0)
        {
            var used = Math.Min(delay, step);
            delay -= used;
            step -= used;
            if (delay > 0)
            {
                return Value;
            }
        }

        elapsed += step;
        var progress = Duration <= 0 ? 1 : Easing.Clamp01(elapsed / Duration);
        Value = From + (To - From) * ease(progress);
        if (progress >= 1)
        {
            Value = To;
            Finished = true;
        }

        return Value;
    }

    public void Jump(double value)
    {
        From = To = Value = value;
        Finished = true;
    }
}

public sealed class Spring
{
    private const double MaxStep = 1.0 / 240;

    public double Stiffness { get; }

    public double Damping { get; }

    public double Position { get; private set; }

    public double Velocity { get; private set; }

    public double Target { get; set; }

    public Spring(double position = 0, double stiffness = 180, double damping = 18)
    {
        Position = Target = position;
        Stiffness = stiffness;
        Damping = damping;
    }

    public bool Settled(double tolerance = 0.01) =>
        Math.Abs(Position - Target) < tolerance && Math.Abs(Velocity) < tolerance;

    public double Step(double deltaSeconds)
    {
        var remaining = Math.Min(Math.Max(0, deltaSeconds), 0.5);
        while (remaining > 0)
        {
            var dt = Math.Min(remaining, MaxStep);
            var acceleration = Stiffness * (Target - Position) - Damping * Velocity;
            Velocity += acceleration * dt;
            Position += Velocity * dt;
            remaining -= dt;
        }

        return Position;
    }

    public void Snap(double value)
    {
        Position = Target = value;
        Velocity = 0;
    }

    public void Kick(double velocity) => Velocity += velocity;
}

public static class Motion
{
    public static double Shake(double seconds, double amplitude, double frequency, double duration)
    {
        if (seconds < 0 || seconds >= duration || duration <= 0)
        {
            return 0;
        }

        var decay = 1 - seconds / duration;
        return amplitude * decay * decay * Math.Sin(seconds * frequency * 2 * Math.PI);
    }

    public static double Pulse(double seconds, double period)
    {
        if (period <= 0)
        {
            return 0;
        }

        var phase = seconds % period / period;
        return 0.5 - 0.5 * Math.Cos(phase * 2 * Math.PI);
    }

    public static double Blink(double seconds, double period, double onFraction)
    {
        if (period <= 0)
        {
            return 0;
        }

        return seconds % period / period < Easing.Clamp01(onFraction) ? 1 : 0;
    }
}
