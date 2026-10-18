namespace IpGuard;

public sealed class IpWatcher
{
    private const int HistoryLimit = 1000;

    private readonly MonitorState state;
    private readonly Func<string> homeCountry;
    private Status? previous;

    public IpWatcher(MonitorState state, Func<string> homeCountry)
    {
        this.state = state;
        this.homeCountry = homeCountry;
    }

    public Verdict Evaluate(IpInfo? info, bool force)
    {
        var verdict = Decide(info, force);
        previous = verdict.Status;
        if (info is not null)
        {
            Record(info, verdict.Status);
        }

        return verdict;
    }

    public void Trust(IpInfo info)
    {
        state.Trusted[info.CountryCode] = info.Ip;
    }

    private Verdict Decide(IpInfo? info, bool force)
    {
        if (info is null)
        {
            return new Verdict(Status.Unavailable, "Нет связи", "Не удалось определить внешний IP", previous != Status.Unavailable);
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

    private void Record(IpInfo info, Status status)
    {
        var last = state.History.Count > 0 ? state.History[^1] : null;
        if (last is null || last.Ip != info.Ip || last.CountryCode != info.CountryCode || last.Status != status)
        {
            state.History.Add(new HistoryEntry(DateTime.Now, info.Ip, info.CountryCode, info.Country, info.Provider, status));
            if (state.History.Count > HistoryLimit)
            {
                state.History.RemoveRange(0, state.History.Count - HistoryLimit);
            }
        }

        state.LastIp = info.Ip;
        state.LastCountryCode = info.CountryCode;
    }
}
