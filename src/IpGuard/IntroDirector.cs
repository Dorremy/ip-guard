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
    private const float SparseStart = 0.22f;
    private const float NameCrowd = 0.4f;
    private const float MoodRate = 1.5f;
    public const int RoamerCount = 130;
    public const int DustStart = RoamStart + RoamerCount;
    public const int SurgeCount = 260;
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
    private float burstAt = -100f;
    private float burstPower;
    private int[] surge = Array.Empty<int>();
    private float surgeEnds;
    private int surgeCursor;
    private readonly Random surgeRandom = new(77);

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

    public float Crowd
    {
        get
        {
            var begin = timing.Chaos * 0.4f;
            if (timing.Name - begin < 0.5f)
            {
                return 1f;
            }

            var toName = Smooth((Time - begin) / (timing.Name - begin));
            var settle = Smooth((Time - timing.Stream) / Math.Max(0.01f, timing.Status - timing.Stream));
            return SparseStart + (NameCrowd - SparseStart) * toName + (1f - NameCrowd) * settle;
        }
    }

    public float FlowScale { get; private set; } = 1f;

    public float Warm { get; private set; }

    public float Glow { get; private set; } = 1f;

    public static float Presence(int index, float crowd)
    {
        var c = float.IsFinite(crowd) ? Math.Clamp(crowd, 0f, 1f) : 1f;
        var rank = index * 0.6180339887f % 1f;
        return Smooth((c * 1.15f - rank) / 0.15f);
    }

    public float Dim => 1f - 0.18f * Smooth((Time - timing.Status - 0.1f) / 0.7f);

    public float DustLevel => 0.6f - 0.25f * Smooth((Time - disperseAt - 0.1f) / 1.0f);

    public bool IsDust(int i) => i >= DustStart;

    public IReadOnlyList<int> SurgeIndices => surge;

    public bool Surging => surge.Length > 0;

    public void Surge(Rectangle area)
    {
        var dust = field.Count - DustStart;
        if (dust <= 0 || area.Width <= 0 || area.Height <= 0 || Scene < Scene.Status)
        {
            return;
        }

        ReleaseSurge();
        var count = Math.Min(SurgeCount, dust);
        var indices = new int[count];
        var points = new PointF[count];
        for (var k = 0; k < count; k++)
        {
            indices[k] = DustStart + (surgeCursor + k) % dust;
            points[k] = new PointF(
                area.X + (float)surgeRandom.NextDouble() * area.Width,
                area.Y + (float)surgeRandom.NextDouble() * area.Height);
        }

        surgeCursor = (surgeCursor + count) % dust;
        field.Assign(indices, points, 0.2f);
        surge = indices;
        surgeEnds = Time + 0.7f;
    }

    private void ReleaseSurge()
    {
        if (surge.Length == 0)
        {
            return;
        }

        field.Release(surge, 45);
        surge = Array.Empty<int>();
    }

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
            burstAt = Time;
            burstPower = Tone switch { Tone.Alert => 1f, Tone.Offline => 0f, _ => 0.5f };
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

        if (surge.Length > 0 && Time >= surgeEnds)
        {
            ReleaseSurge();
        }

        Mood(dt);
        field.Step(dt);
    }

    private void Mood(float dt)
    {
        var tone = Tone;
        var checking = Checking && didStatus ? 1f : 0f;
        var speed = tone switch { Tone.Alert => 1.25f, Tone.Offline => 0.45f, _ => 1f };
        var warm = tone == Tone.Alert ? 0.65f : 0f;
        var glow = tone switch { Tone.Alert => 1.15f, Tone.Offline => 0.25f, _ => 1f };
        var since = Time - burstAt;
        var burst = since < 0f || !didStatus ? 0f : Smooth(since / 0.25f) * MathF.Exp(-MathF.Max(0f, since - 0.25f) / 1.1f) * burstPower;
        speed *= (1f + 0.7f * checking) * (1f + 0.8f * burst);
        var k = 1f - MathF.Exp(-dt * MoodRate);
        FlowScale += (speed - FlowScale) * k;
        Warm += (warm - Warm) * k;
        Glow += (glow - Glow) * k;
        var packet = 0.5f + 0.5f * MathF.Sin(Time * 2.3f);
        var pull = 0.3f * checking * (0.25f + 0.75f * packet) + 1.1f * burst;
        field.FlowScale = FlowScale;
        field.SetPull(field.Width / 2f, NebulaLayout.StripY, didStatus ? pull : 0f);
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
        surge = Array.Empty<int>();
        burstAt = -100f;
        FlowScale = 1f;
        Warm = 0f;
        Glow = 1f;
        field.FlowScale = 1f;
        field.SetPull(0, 0, 0);
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
