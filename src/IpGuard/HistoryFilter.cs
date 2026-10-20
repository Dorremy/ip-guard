using System.Text;

namespace IpGuard;

public static class HistoryFilter
{
    public static IReadOnlyList<HistoryEntry> Apply(IEnumerable<HistoryEntry> history, string? country, Status? status, string? text)
    {
        var needle = text?.Trim() ?? "";
        var result = new List<HistoryEntry>();
        foreach (var entry in history.Reverse())
        {
            if (country is not null && entry.CountryCode != country)
            {
                continue;
            }

            if (status is not null && entry.Status != status)
            {
                continue;
            }

            if (needle.Length > 0 && !Matches(entry, needle))
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }

    public static IReadOnlyList<string> CountriesIn(IEnumerable<HistoryEntry> history) =>
        history.Select(entry => entry.CountryCode).Where(code => code.Length > 0).Distinct().OrderBy(code => code, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<Status> StatusesIn(IEnumerable<HistoryEntry> history) =>
        history.Select(entry => entry.Status).Distinct().OrderBy(status => (int)status).ToList();

    public static string ToCsv(IEnumerable<HistoryEntry> entries)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(";", "Время", "IP", "Код страны", "Страна", "Провайдер", "Статус", "Примечание")).Append("\r\n");
        foreach (var entry in entries)
        {
            builder.Append(string.Join(
                ";",
                Quote(entry.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                Quote(entry.Ip),
                Quote(entry.CountryCode),
                Quote(entry.Country),
                Quote(entry.Provider),
                Quote(Labels.For(entry.Status)),
                Quote(entry.Note ?? ""))).Append("\r\n");
        }

        return builder.ToString();
    }

    public static void Save(string path, IEnumerable<HistoryEntry> entries) =>
        File.WriteAllText(path, ToCsv(entries), new UTF8Encoding(true));

    private static bool Matches(HistoryEntry entry, string needle)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return entry.Ip.Contains(needle, comparison)
            || entry.Provider.Contains(needle, comparison)
            || entry.Country.Contains(needle, comparison)
            || entry.CountryCode.Contains(needle, comparison)
            || (entry.Note?.Contains(needle, comparison) ?? false)
            || Labels.For(entry.Status).Contains(needle, comparison);
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
