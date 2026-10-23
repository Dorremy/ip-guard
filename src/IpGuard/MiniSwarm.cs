using System.Drawing.Imaging;

namespace IpGuard;

public sealed class MiniSwarm : IDisposable
{
    private const float TrailKeep = 0.968f;
    private const float TrailWeight = 0.3f;
    private const float TrailGain = 0.9f;
    private static readonly Color Pale = Color.FromArgb(205, 216, 235);

    private readonly ParticleField field;
    private readonly TrailLayer trail = new();
    private readonly SwarmPainter painter = new();
    private readonly CurtainCache curtain = new(60, 24);
    private readonly float width;
    private readonly float height;
    private float lastTime = -1f;

    public MiniSwarm(int count, float width, float height, int seed)
    {
        field = new ParticleField(count, width, height, seed);
        this.width = width;
        this.height = height;
    }

    public ParticleField Field => field;

    public void Step(float dt) => field.Step(dt);

    public void Render(Bitmap target, float time, float warm, float level)
    {
        var w = target.Width;
        var h = target.Height;
        if (w < 8 || h < 8)
        {
            return;
        }

        var t = float.IsFinite(time) ? time : 0f;
        var strength = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 1f;
        var tint = Gfx.Mix(Pale, NebulaRenderer.ToneColor(float.IsFinite(warm) && warm > 0.5f ? Tone.Alert : Tone.Calm), 0.35f);
        var sx = w / width;
        var sy = h / height;
        trail.Resize(w, h);
        var elapsed = t - lastTime;
        lastTime = t;
        if (elapsed < 0f || elapsed > 0.5f)
        {
            trail.Clear();
            elapsed = 0f;
        }

        trail.Decay(MathF.Pow(TrailKeep, elapsed * 30f));
        using (var g = Graphics.FromImage(target))
        {
            curtain.Draw(g, w, h, t, 1f, float.IsFinite(warm) ? warm : 0f, 0.9f);
        }

        painter.Begin(target, sx);
        painter.Splat(field, sx, sy, t, i =>
        {
            var edge = MathF.Min(MathF.Min(field.X[i], width - field.X[i]), MathF.Min(field.Y[i], height - field.Y[i]));
            var fade = edge < 10f ? Gfx.Smooth(edge / 10f) : 1f;
            var b = field.Brightness[i] * (0.3f + 0.7f * field.Depth[i]) * fade * strength;
            trail.Deposit(field.X[i] * sx, field.Y[i] * sy, b * TrailWeight);
            return new Dot(b * 0.4f, tint, 0.08f, 0f);
        });
        painter.AddTrail(trail.Data, trail.Width, trail.Height, tint, TrailGain);
        painter.End();
    }

    public void Dispose()
    {
        curtain.Dispose();
    }
}
