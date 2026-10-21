namespace IpGuard;

public sealed class AmbientScene
{
    public const int DefaultCount = 1800;
    public const int RoamerCount = 90;

    private readonly int[] roamers;

    public AmbientScene(int width, int height, int seed, int count = DefaultCount)
    {
        Field = new ParticleField(count, width, height, seed);
        roamers = Enumerable.Range(0, Math.Min(RoamerCount, count)).ToArray();
        Field.Roam(roamers);
    }

    public ParticleField Field { get; }

    public IReadOnlyList<int> Roamers => roamers;

    public float Time { get; private set; }

    public float Dim { get; set; } = 1f;

    public Tone Tone { get; set; } = Tone.Calm;

    public float Appear => Gfx.Smooth(Time / 0.8f);

    public float LinkLevel => Gfx.Smooth((Time - 0.3f) / 1.0f);

    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0)
        {
            return;
        }

        var h = Math.Min(dt, 1f);
        Time += h;
        Field.Step(h);
    }
}
