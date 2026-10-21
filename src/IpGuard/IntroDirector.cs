namespace IpGuard;

public enum Scene
{
    Opening,
    Chaos,
    Name,
    Disperse,
    Stream,
    Status,
    Menu,
    Idle
}

public enum IntroKind
{
    Full,
    Short,
    None
}

public sealed record IntroTargets(PointF[] Name, PointF[] Logo);

public sealed class IntroDirector
{
    public const int NameCount = 1200;
    public const int LogoCount = 600;
    public const int StatusCount = 1600;
    public const int RoamStart = StatusCount;
    public const int RoamerCount = 130;
    public const int DustStart = RoamStart + RoamerCount;
    private const float MaxStep = 1f;
    private const float SubStep = 1f / 30f;
    private const float ToneSeconds = 0.8f;

    private sealed record Timing(float Chaos, float Name, float Disperse, float Stream, float Status, float Menu, float Idle);

    private static readonly Timing FullTiming = new(0.45f, 1.5f, 3.1f, 3.3f, 4.8f, 5.3f, 6.3f);
    private static readonly Timing ShortTiming = new(0.25f, 0.25f, 0.25f, 0.25f, 1.05f, 1.25f, 1.9f);

    private readonly ParticleField field;
    private readonly IntroTargets targets;
    private readonly Func<PanelState, PointF[]> statusTargets;
    private readonly int[] nameIndices = Enumerable.Range(0, NameCount).ToArray();
    private readonly int[] logoIndices = Enumerable.Range(NameCount, LogoCount).ToArray();
    private readonly int[] disperseIndices = Enumerable.Range(0, NameCount + LogoCount).ToArray();
    private readonly int[] statusIndices = Enumerable.Range(0, StatusCount).ToArray();
    private readonly int[] roamIndices = Enumerable.Range(RoamStart, RoamerCount).ToArray();
    private readonly float[] lineU = new float[StatusCount];
    private readonly PointF[] lineNoise = new PointF[StatusCount];
    private readonly PointF[] streamBuffer = new PointF[StatusCount];
    private Timing timing = FullTiming;
    private bool didName;
    private bool didDisperse;
    private bool didStream;
    private bool didStatus;
    private bool awaitingFirst;
    private float toneStart;
    private float disperseAt;

    public IntroDirector(ParticleField field, IntroTargets targets, Func<PanelState, PointF[]> statusTargets, IntroKind kind)
    {
        if (field.Count < DustStart)
        {
            throw new ArgumentException("Слишком мало частиц для заставки");
        }

        this.field = field;
        this.targets = targets;
        this.statusTargets = statusTargets;
        var rng = new Random(31);
        for (var i = 0; i < StatusCount; i++)
        {
            lineU[i] = (float)rng.NextDouble();
            lineNoise[i] = new PointF(Gauss(rng), Gauss(rng));
        }

        Begin(kind);
    }

    public ParticleField Field => field;

    public float Time { get; private set; }

    public PanelState State { get; private set; } = PanelState.Initial;

    public Tone Tone => PanelState.ToneOf(State.Status);

    public Tone? PreviousTone { get; private set; }

    public float Blend => Smooth((Time - toneStart) / ToneSeconds);

    public float Flash { get; private set; }

    public int Reforms { get; private set; }

    public IReadOnlyList<int> Roamers => roamIndices;

    public Scene Scene
    {
        get
        {
            var t = timing;
            if (Time < t.Chaos) return Scene.Opening;
            if (Time < t.Name) return Scene.Chaos;
            if (Time < t.Disperse) return Scene.Name;
            if (Time < t.Stream) return Scene.Disperse;
            if (Time < t.Status) return Scene.Stream;
            if (Time < t.Menu) return Scene.Status;
            if (Time < t.Idle) return Scene.Menu;
            return Scene.Idle;
        }
    }

    public float MenuProgress => Time < timing.Menu ? 0f : EaseOut((Time - timing.Menu) / 1.0f);

    public float InfoAlpha => Smooth((Time - timing.Status) / 0.7f);

    public float Dim => 1f - 0.18f * Smooth((Time - timing.Status - 0.1f) / 0.7f);

    public float DustLevel => 1f - 0.65f * Smooth((Time - disperseAt - 0.1f) / 1.0f);

    public bool IsDust(int i) => i >= DustStart;

    public bool Checking { get; set; }

    public float LinkLevel => Smooth((Time - timing.Stream - 0.3f) / 1.0f);

    public float StreamCapsule
    {
        get
        {
            var begin = timing.Stream + 0.47f * (timing.Status - timing.Stream);
            return Smooth((Time - begin) / Math.Max(0.01f, timing.Status - begin));
        }
    }

    public void Restart(IntroKind kind)
    {
        if (kind == IntroKind.None)
        {
            Begin(kind);
            return;
        }

        field.Scatter();
        Begin(kind);
    }

    public void SetState(PanelState state)
    {
        if (state == State)
        {
            return;
        }

        var changed = state.Status != State.Status || state.Label != State.Label;
        var oldTone = Tone;
        State = state;
        if (awaitingFirst)
        {
            awaitingFirst = false;
            Form(snap: true);
            PreviousTone = null;
            toneStart = -100f;
            return;
        }

        if (!didStatus)
        {
            return;
        }

        if (Tone != oldTone)
        {
            PreviousTone = oldTone;
            toneStart = Time;
        }

        if (changed)
        {
            Reforms++;
            Flash = 1f;
            field.Release(statusIndices, 18);
            field.Assign(statusIndices, statusTargets(State), 0.25f);
        }
    }

    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0)
        {
            return;
        }

        var remaining = Math.Min(dt, MaxStep);
        while (remaining > 1e-6f)
        {
            var h = Math.Min(remaining, SubStep);
            Advance(h);
            remaining -= h;
        }
    }

    private void Advance(float dt)
    {
        Time += dt;
        Flash = Math.Max(0f, Flash - dt / 0.6f);
        var t = timing;
        if (!didName && Time >= t.Name)
        {
            didName = true;
            field.Assign(nameIndices, targets.Name, 0.4f);
            field.Assign(logoIndices, targets.Logo, 0.4f);
        }

        if (!didDisperse && Time >= t.Disperse)
        {
            didDisperse = true;
            disperseAt = Time;
            field.Release(disperseIndices, 70);
        }

        if (!didStream && Time >= t.Stream)
        {
            didStream = true;
            field.Roam(roamIndices);
        }

        if (didStream && !didStatus && Time >= t.Status)
        {
            didStatus = true;
            toneStart = Time;
            PreviousTone = null;
            field.Assign(statusIndices, statusTargets(State), 0.5f);
        }
        else if (didStream && !didStatus)
        {
            Stream();
        }

        field.Step(dt);
    }

    private void Stream()
    {
        var capsule = StreamCapsule;
        var progress = Smooth((Time - timing.Stream) / 0.7f);
        var center = new PointF(field.Width / 2, NebulaLayout.StripY);
        var width = 0.9f * field.Width;
        for (var i = 0; i < StatusCount; i++)
        {
            streamBuffer[i] = TargetSampler.StreamPoint(lineU[i], Time, capsule, center, width, lineNoise[i]);
        }

        field.Follow(statusIndices, streamBuffer);
        field.SetStiffness(statusIndices, 14f + 22f * progress);
    }

    private void Begin(IntroKind kind)
    {
        Time = 0;
        Flash = 0;
        didName = false;
        didDisperse = false;
        didStream = false;
        didStatus = false;
        awaitingFirst = false;
        toneStart = 1e9f;
        PreviousTone = null;
        disperseAt = 0;
        timing = kind == IntroKind.Short ? ShortTiming : FullTiming;
        if (timing.Disperse <= timing.Name)
        {
            didName = true;
            didDisperse = true;
        }

        if (kind != IntroKind.None)
        {
            return;
        }

        Time = timing.Idle + 10f;
        didName = true;
        didDisperse = true;
        didStream = true;
        didStatus = true;
        disperseAt = -10f;
        toneStart = -100f;
        awaitingFirst = true;
        field.Roam(roamIndices);
        Form(snap: true);
    }

    private void Form(bool snap)
    {
        field.Assign(statusIndices, statusTargets(State), 0f);
        if (snap)
        {
            field.Snap(statusIndices);
        }
    }

    private static float Smooth(float x)
    {
        var t = Math.Clamp(x, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOut(float x)
    {
        var t = Math.Clamp(x, 0f, 1f);
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    private static float Gauss(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
