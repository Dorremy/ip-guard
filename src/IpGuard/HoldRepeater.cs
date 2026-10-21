namespace IpGuard;

public sealed class HoldRepeater
{
    private const double Delay = 0.4;
    private const double Rate = 15.0;
    private const int MaxPulses = 30;

    private double start;
    private double next;

    public bool Active { get; private set; }

    public bool Repeated { get; private set; }

    public void Begin(double now)
    {
        Active = true;
        Repeated = false;
        start = now;
        next = now + Delay;
    }

    public void Cancel()
    {
        Active = false;
    }

    public int Multiplier(double now)
    {
        var held = now - start;
        return held > 3.0 ? 20 : held > 1.5 ? 5 : 1;
    }

    public int Pulses(double now)
    {
        if (!Active || now < next)
        {
            return 0;
        }

        var count = (int)Math.Min(MaxPulses, Math.Floor((now - next) * Rate) + 1);
        next += count / Rate;
        if (next < now)
        {
            next = now + 1.0 / Rate;
        }

        Repeated = true;
        return count;
    }
}
