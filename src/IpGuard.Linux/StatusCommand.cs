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

        var age = context.UtcNow() - report.UpdatedUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

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

    private static string Place(Report report)
    {
        if (string.IsNullOrEmpty(report.Ip))
        {
            return "Нет связи";
        }

        var country = string.IsNullOrEmpty(report.Country) ? report.CountryCode : report.Country;
        return string.Join(" · ", new[] { report.Ip, country, report.Provider }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool TryRead(string path, out Report report)
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

            report = new Report(
                ok.GetBoolean(),
                Text(root, "title"),
                Text(root, "ip"),
                Text(root, "countryCode"),
                Text(root, "country"),
                Text(root, "provider"),
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

    private sealed record Report(bool Ok, string Title, string Ip, string CountryCode, string Country, string Provider, DateTime UpdatedUtc);
}
