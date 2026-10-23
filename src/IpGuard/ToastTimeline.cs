namespace IpGuard;

public sealed class ToastTimeline
{
    public const float In = 0.35f;
    public const float Out = 0.35f;
    private const float MaxHold = 60f;

    private float hold;
    private float age;
    private bool hovering;

    public float Age => age;

    public bool Hovering
    {
        get => hovering;
        set => hovering = value;
    }

    public float Appear => Ease(age / In);

    public float Alpha => Math.Min(Ease(age / In), Ease((In + hold + Out - age) / Out));

    public bool Finished => age >= In + hold + Out;

    public void Start(float holdSeconds)
    {
        hold = float.IsFinite(holdSeconds) ? Math.Clamp(holdSeconds, 0f, MaxHold) : 0f;
        age = 0f;
    }

    public void Dismiss()
    {
        age = Math.Max(age, In + hold);
    }

    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        var step = Math.Min(dt, 0.25f);
        if (hovering)
        {
            if (age > In + hold)
            {
                age = In + hold * 0.6f;
            }

            if (age >= In)
            {
                return;
            }
        }

        age += step;
    }

    private static float Ease(float v)
    {
        var c = Math.Clamp(v, 0f, 1f);
        return c * c * (3f - 2f * c);
    }
}
