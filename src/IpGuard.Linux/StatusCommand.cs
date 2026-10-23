using System.Text.Json;

namespace IpGuard;

public static class StatusCommand
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(2);

    public static int Run(CliContext context)
    {
        var path = context.Paths.Status;
        if (!File.Exists(path))
        {
            context.Out.WriteLine("Служба ещё не запускалась");
            return 1;
        }

        if (!TryRead(path, out var report))
        {
            context.Out.WriteLine("Файл статуса не читается");
            return 1;
        }

        var age = AgeOf(report, context.UtcNow());
        var stale = age >= FreshFor;
        context.Out.WriteLine(report.Title);
        context.Out.WriteLine(Place(report));
        context.Out.WriteLine($"Обновлено {Ago(age)}");
        if (stale)
        {
            context.Out.WriteLine("Статус устарел: служба не отвечает");
        }

        return report.Ok && !stale ? 0 : 1;
    }

    public static string Ago(TimeSpan age)
    {
        if (age.TotalSeconds < 60)
        {
            return $"{(int)age.TotalSeconds} с назад";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{(int)age.TotalMinutes} мин назад";
        }

        if (age.TotalHours < 48)
        {
            return $"{(int)age.TotalHours} ч назад";
        }

        return $"{(int)age.TotalDays} дн назад";
    }

    public static TimeSpan AgeOf(StatusReport report, DateTime nowUtc)
    {
        var age = nowUtc - report.UpdatedUtc;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    public static string Place(StatusReport report)
    {
        if (string.IsNullOrEmpty(report.Ip))
        {
            return "Нет связи";
        }

        var country = string.IsNullOrEmpty(report.Country) ? report.CountryCode : report.Country;
        return string.Join(" · ", new[] { report.Ip, country, report.Provider }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static bool TryRead(string path, out StatusReport report)
    {
        report = default!;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ok", out var ok)
                || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("updatedUtc", out var updated)
                || updated.ValueKind != JsonValueKind.String
                || !updated.TryGetDateTime(out var time))
            {
                return false;
            }

            report = new StatusReport(
                ok.GetBoolean(),
                Text(root, "state"),
                Text(root, "title"),
                Text(root, "message"),
                Text(root, "ip"),
                Text(root, "countryCode"),
                Text(root, "country"),
                Text(root, "provider"),
                Text(root, "leak"),
                Texts(root, "references"),
                time.ToUniversalTime());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static IReadOnlyList<string> Texts(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .ToList();
    }
}

public sealed record StatusReport(
    bool Ok,
    string State,
    string Title,
    string Message,
    string Ip,
    string CountryCode,
    string Country,
    string Provider,
    string Leak,
    IReadOnlyList<string> References,
    DateTime UpdatedUtc);
