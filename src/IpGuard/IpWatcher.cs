namespace IpGuard;

public sealed class IpWatcher
{
    public static readonly TimeSpan DefaultOutageThreshold = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MinRecordedOutage = TimeSpan.FromSeconds(5);

    private const int HistoryLimit = 1000;

    private readonly MonitorState state;
    private readonly Func<string> homeCountry;
    private readonly Func<DateTime> now;
    private readonly Func<TimeSpan> outageThreshold;
    private Status? previous;
    private DateTime? offlineSince;
    private bool offlineAlerted;

    public string? Leak { get; private set; }

    public TimeSpan? LastOutage { get; private set; }

    public TimeSpan? CurrentOutage => offlineSince is { } since ? now() - since : null;

    public IpWatcher(MonitorState state, Func<string> homeCountry, Func<DateTime>? now = null, Func<TimeSpan>? outageThreshold = null)
    {
        this.state = state;
        this.homeCountry = homeCountry;
        this.now = now ?? (() => DateTime.Now);
        this.outageThreshold = outageThreshold ?? (() => DefaultOutageThreshold);
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

        if (restored is { } outage && (outage.Alerted || (outage.Span >= outageThreshold() && outage.Span >= MinRecordedOutage)))
        {
            LastOutage = outage.Span;
            verdict = verdict with
            {
                Notify = true,
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

    public Verdict EvaluateProxyDown(IpInfo direct, bool force)
    {
        offlineSince = null;
        offlineAlerted = false;
        LastOutage = null;

        var message = $"Прокси не отвечает, а напрямую интернет есть: {direct.Country} ({direct.CountryCode}), {direct.Ip}. Программы без прокси выходят напрямую";
        var notify = force || previous != Status.Leak || Leak != message;
        Leak = message;
        previous = Status.Leak;
        Record(direct, Status.Leak, message);
        return new Verdict(Status.Leak, "Прокси не отвечает!", message, notify);
    }

    public static string DescribeReferences(IEnumerable<Reference> references) =>
        string.Join(", ", references.Select(reference => reference.Label.Length > 0 ? $"{reference.Ip} ({reference.Label})" : reference.Ip));

    public IReadOnlyList<Reference> ReferencesFor(IpInfo info) =>
        info.CountryCode == homeCountry() ? Array.Empty<Reference>() : state.ReferencesFor(info.CountryCode);

    public bool IsReference(IpInfo info) => ReferencesFor(info).Any(reference => reference.Ip == info.Ip);

    public bool IsMismatch(IpInfo info)
    {
        var references = ReferencesFor(info);
        return references.Count > 0 && references.All(reference => reference.Ip != info.Ip);
    }

    public bool Trust(IpInfo info, string label = "") =>
        state.AddReference(new Reference(info.CountryCode, info.Ip, label.Trim()));

    private Verdict Decide(IpInfo? info, bool force, DateTime moment)
    {
        if (info is null)
        {
            var threshold = outageThreshold();
            var since = offlineSince ?? moment;
            if (moment - since < threshold)
            {
                return new Verdict(Status.Unavailable, "Нет связи", "Не удалось определить внешний IP", force);
            }

            var first = !offlineAlerted;
            offlineAlerted = true;
            return new Verdict(
                Status.Unavailable,
                threshold < TimeSpan.FromSeconds(1) ? "Нет связи" : $"Нет связи больше {FormatDuration(threshold)}",
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

        var references = state.ReferencesFor(info.CountryCode);
        if (references.Count == 0)
        {
            var pending = force || previous != Status.NoReference || state.LastIp != info.Ip;
            return new Verdict(
                Status.NoReference,
                "Эталон не задан",
                $"{place}. Чтобы запомнить этот IP, выберите в меню трея «Принять текущий IP как эталон»",
                pending);
        }

        var match = references.FirstOrDefault(reference => reference.Ip == info.Ip);
        if (match is not null)
        {
            var notify = force
                || previous != Status.Unchanged
                || state.LastCountryCode != info.CountryCode
                || state.LastIp != info.Ip;
            var label = match.Label.Length > 0 ? $" ({match.Label})" : "";
            return new Verdict(Status.Unchanged, "Готов к работе", $"{place}. IP совпадает с эталонным{label}", notify);
        }

        var alert = force || previous != Status.Changed || state.LastIp != info.Ip;
        return new Verdict(
            Status.Changed,
            "IP отличается от эталонного!",
            $"Сейчас: {place}. {(references.Count == 1 ? "Эталонный IP" : "Эталонные IP")}: {DescribeReferences(references)}",
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
