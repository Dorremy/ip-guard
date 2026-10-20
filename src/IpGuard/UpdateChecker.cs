using System.Net;
using System.Text.Json;

namespace IpGuard;

public sealed record ReleaseInfo(Version Version, string Tag, string Url);

public static class Versions
{
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = text.Trim().TrimStart('v', 'V');
        var cut = clean.IndexOfAny(new[] { '+', '-' });
        if (cut >= 0)
        {
            clean = clean[..cut];
        }

        var parts = clean.Split('.');
        if (parts.Length is < 2 or > 3 || !parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)))
        {
            return null;
        }

        return Version.TryParse(parts.Length == 2 ? clean + ".0" : clean, out var version) ? version : null;
    }

    public static bool IsNewer(Version candidate, Version current) => candidate > current;
}

public readonly record struct UpdateOutcome(ReleaseInfo? Available, bool Failed, bool Notify, string Message);

public sealed class UpdateChecker
{
    public const string ApiUrl = "https://api.github.com/repos/Dorremy/ip-guard/releases/latest";
    public const string ReleasePrefix = "https://github.com/Dorremy/ip-guard/";
    public const string FallbackPage = "https://github.com/Dorremy/ip-guard/releases";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly Func<Task<string?>> fetch;

    public UpdateChecker(Func<Task<string?>>? fetch = null)
    {
        this.fetch = fetch ?? (() => Download(ApiUrl, SystemProxy.Current));
    }

    public static bool ShouldCheck(DateTime? lastUtc, DateTime nowUtc) =>
        lastUtc is not { } last || last > nowUtc.AddHours(1) || nowUtc - last >= Interval;

    public static UpdateOutcome Evaluate(ReleaseInfo? latest, Version current, string notifiedTag, bool manual)
    {
        if (latest is null)
        {
            return new UpdateOutcome(null, true, manual, "Не удалось проверить обновления. Проверьте подключение к интернету");
        }

        if (Versions.IsNewer(latest.Version, current))
        {
            return new UpdateOutcome(latest, false, manual || notifiedTag != latest.Tag, $"Вышла новая версия {latest.Tag}. Откройте пункт меню, чтобы перейти на страницу загрузки");
        }

        return new UpdateOutcome(null, false, manual, "Установлена последняя версия");
    }

    public static bool IsSafeUrl(string? url) =>
        url is not null && url.StartsWith(ReleasePrefix, StringComparison.Ordinal) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public static ReleaseInfo? ParseRelease(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Flag(root, "draft")
                || Flag(root, "prerelease")
                || !root.TryGetProperty("tag_name", out var tagElement)
                || tagElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var tag = tagElement.GetString() ?? "";
            var version = Versions.Parse(tag);
            if (version is null)
            {
                return null;
            }

            var url = root.TryGetProperty("html_url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString()
                : null;
            return new ReleaseInfo(version, tag, IsSafeUrl(url) ? url! : FallbackPage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<ReleaseInfo?> LatestAsync() => ParseRelease(await fetch());

    public static async Task<string?> Download(string url, Func<IWebProxy?> proxy)
    {
        try
        {
            using var client = IpLookup.CreateClient(proxy());
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
