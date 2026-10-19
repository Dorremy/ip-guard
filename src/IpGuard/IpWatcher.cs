namespace IpGuard;

public sealed class IpWatcher
{
    public static readonly TimeSpan OutageThreshold = TimeSpan.FromMinutes(1);

    private const int HistoryLimit = 1000;

    private readonly MonitorState state;
    private readonly Func<string> homeCountry;
    private readonly Func<DateTime> now;
    private Status? previous;
    private DateTime? offlineSince;
    private bool offlineAlerted;

    public string? Leak { get; private set; }

    public TimeSpan? LastOutage { get; private set; }

    public IpWatcher(MonitorState state, Func<string> homeCountry, Func<DateTime>? now = null)
    {
        this.state = state;
        this.homeCountry = homeCountry;
        this.now = now ?? (() => DateTime.Now);
    }

    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} ч {span.Minutes} мин";
        }

        if (span.TotalMinutes >= 1)
        {
            return span.Seconds == 0 ? $"{span.Minutes} мин" : $"{span.Minutes} мин {span.Seconds} с";
        }

        return $"{Math.Max(0, (int)span.TotalSeconds)} с";
    }

    public Verdict Evaluate(IpInfo? info, bool force, string? leak = null)
    {
        var moment = now();
        LastOutage = null;
        var restored = TrackOutage(info, moment);
        var verdict = Decide(info, force, moment);
        if (info is not null && verdict.Status is Status.Unchanged or Status.NoReference or Status.Changed)
        {
            if (leak is not null && verdict.Status != Status.Changed)
            {
                verdict = new Verdict(Status.Leak, "Утечка мимо VPN!", leak, force || leak != Leak);
            }
            else if (leak is null && Leak is not null)
            {
                verdict = verdict with { Notify = true };
            }

            Leak = leak;
        }
        else
        {
            Leak = null;
            leak = null;
        }

        if (restored is { } outage && outage.Span >= OutageThreshold)
        {
            LastOutage = outage.Span;
            verdict = verdict with
            {
                Notify = verdict.Notify || outage.Alerted,
                Message = $"{verdict.Message}. Связи не было {FormatDuration(outage.Span)}"
            };
            RecordOutage(outage.Since, outage.Span);
        }

        if (verdict.Status != Status.Unavailable || offlineAlerted)
        {
            previous = verdict.Status;
        }

        if (info is not null)
        {
            Record(info, verdict.Status, leak);
        }

        return verdict;
    }

    private (DateTime Since, TimeSpan Span, bool Alerted)? TrackOutage(IpInfo? info, DateTime moment)
    {
        if (info is null)
        {
            offlineSince ??= moment;
            return null;
        }

        if (offlineSince is not { } since)
        {
            return null;
        }

        var result = (since, moment - since, offlineAlerted);
        offlineSince = null;
        offlineAlerted = false;
        return result;
    }

    private void RecordOutage(DateTime since, TimeSpan span)
    {
        state.History.Add(new HistoryEntry(
            since,
            state.LastIp ?? "",
            state.LastCountryCode ?? "",
            "",
            "",
            Status.Unavailable,
            $"Связи не было {FormatDuration(span)}"));
        if (state.History.Count > HistoryLimit)
        {
            state.History.RemoveRange(0, state.History.Count - HistoryLimit);
        }
    }

    public string? ReferenceFor(IpInfo info) =>
        info.CountryCode != homeCountry() && state.Trusted.TryGetValue(info.CountryCode, out var trusted) ? trusted : null;

    public bool IsMismatch(IpInfo info) => ReferenceFor(info) is { } trusted && trusted != info.Ip;

    public void Trust(IpInfo info)
    {
        state.Trusted[info.CountryCode] = info.Ip;
    }

    private Verdict Decide(IpInfo? info, bool force, DateTime moment)
    {
        if (info is null)
        {
            var since = offlineSince ?? moment;
            if (moment - since < OutageThreshold)
            {
                return new Verdict(Status.Unavailable, "Нет связи", "Не удалось определить внешний IP", force);
            }

            var first = !offlineAlerted;
            offlineAlerted = true;
            return new Verdict(
                Status.Unavailable,
                "Нет связи больше минуты",
                $"Нет связи с {since:HH:mm:ss}. Не удалось определить внешний IP",
                force || first);
        }

        var place = $"{info.Country} ({info.CountryCode}), {info.Ip}";
        var home = homeCountry();

        if (info.CountryCode == home)
        {
            var wasAbroad = state.LastCountryCode is { } last && last != home;
            return wasAbroad
                ? new Verdict(Status.VpnOff, "VPN отключился", place, true)
                : new Verdict(Status.Home, "VPN не подключён", place, force);
        }

        if (!state.Trusted.TryGetValue(info.CountryCode, out var trusted))
        {
            var pending = force || previous != Status.NoReference || state.LastIp != info.Ip;
            return new Verdict(
                Status.NoReference,
                "Эталон не задан",
                $"{place}. Чтобы запомнить этот IP, выберите в меню трея «Принять текущий IP как эталон»",
                pending);
        }

        if (trusted == info.Ip)
        {
            var notify = force
                || previous != Status.Unchanged
                || state.LastCountryCode != info.CountryCode;
            return new Verdict(Status.Unchanged, "Готов к работе", $"{place}. IP совпадает с эталонным", notify);
        }

        var alert = force || previous != Status.Changed || state.LastIp != info.Ip;
        return new Verdict(
            Status.Changed,
            "IP отличается от эталонного!",
            $"Сейчас: {place}. Эталонный IP: {trusted}",
            alert);
    }

    private void Record(IpInfo info, Status status, string? note)
    {
        var last = state.History.Count > 0 ? state.History[^1] : null;
        if (last is null || last.Ip != info.Ip || last.CountryCode != info.CountryCode || last.Status != status || last.Note != note)
        {
            state.History.Add(new HistoryEntry(DateTime.Now, info.Ip, info.CountryCode, info.Country, info.Provider, status, note));
            if (state.History.Count > HistoryLimit)
            {
                state.History.RemoveRange(0, state.History.Count - HistoryLimit);
            }
        }

        state.LastIp = info.Ip;
        state.LastCountryCode = info.CountryCode;
    }
}
