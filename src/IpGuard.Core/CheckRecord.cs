namespace IpGuard;

public static class CheckRecord
{
    public static string Summary(Verdict verdict, IpInfo? info, bool proxied, IReadOnlyList<string> pausedSources)
    {
        var place = info is null ? "адрес не определён" : $"{info.Ip} {info.CountryCode}";
        var route = proxied ? "через прокси" : "без прокси";
        var paused = pausedSources.Count == 0 ? "" : $" | на паузе: {string.Join(", ", pausedSources)}";
        return $"{Labels.For(verdict.Status)} | {place} | {route}{paused}";
    }

    public static string Line(string reason, string summary, Verdict verdict, IpInfo? info, string? leak, TimeSpan? lastOutage, string? disagreement)
    {
        var leakPart = leak is null ? "" : $" | {leak}";
        var alert = verdict.Notify ? " | уведомление" : "";
        if (lastOutage is { } outage)
        {
            alert += $" | связи не было {IpWatcher.FormatDuration(outage)}";
        }

        if (disagreement is not null)
        {
            alert += $" | источники расходятся: {disagreement}";
        }

        var provider = string.IsNullOrEmpty(info?.Provider) ? "" : $" | {info.Provider}";
        return $"{reason}: {summary}{provider}{leakPart}{alert}";
    }
}
