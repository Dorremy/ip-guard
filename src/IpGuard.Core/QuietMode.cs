namespace IpGuard;

public sealed class QuietMode
{
    private readonly Func<DateTime> now;
    private DateTime? until;

    public QuietMode(Func<DateTime>? now = null)
    {
        this.now = now ?? (() => DateTime.Now);
    }

    public bool Active => until is { } end && now() < end;

    public DateTime? Until => Active ? until : null;

    public void Start(TimeSpan duration)
    {
        until = now() + duration;
    }

    public void Stop()
    {
        until = null;
    }

    public string Title => Until is { } end ? $"Не беспокоить (до {end:HH:mm})" : "Не беспокоить";
}

public readonly record struct NotificationPlan(bool Window, bool Balloon, bool Sound);

public static class NotificationPolicy
{
    public static NotificationPlan Decide(Verdict verdict, bool alertWindowEnabled, bool soundEnabled, bool quiet)
    {
        var alarm = verdict.Status is Status.Changed or Status.Leak;
        var window = verdict.Notify && alarm && alertWindowEnabled;
        var loud = verdict.Notify && !window && !quiet;
        return new NotificationPlan(window, loud, loud && soundEnabled);
    }
}
