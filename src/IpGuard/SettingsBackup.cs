using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IpGuard;

public sealed record BackupData(Settings Settings, IReadOnlyList<Reference> References);

public sealed record BackupResult(BackupData? Data, string? Error, IReadOnlyList<string> Notes);

public static class SettingsBackup
{
    public const string AppMarker = "IpGuard";
    public const int FormatVersion = 1;
    public const int MaxBytes = 256 * 1024;
    public const int MaxReferences = 200;
    public const int MaxPrograms = 30;
    public const int MaxLabel = 40;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly int[] OutageChoices = { 0, 15, 30, 60 };

    public static string Export(Settings settings, IEnumerable<Reference> references, DateTime? nowUtc = null)
    {
        var copy = new Settings();
        copy.CopyFrom(settings);
        copy.LastUpdateCheckUtc = null;
        copy.NotifiedUpdate = "";

        var root = new JsonObject
        {
            ["app"] = AppMarker,
            ["formatVersion"] = FormatVersion,
            ["exportedUtc"] = (nowUtc ?? DateTime.UtcNow).ToString("o"),
            ["settings"] = JsonSerializer.SerializeToNode(copy, Options),
            ["references"] = JsonSerializer.SerializeToNode(references.ToList(), Options)
        };
        return root.ToJsonString(Options);
    }

    public static BackupResult Import(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail("Файл пустой");
        }

        if (json.Length > MaxBytes)
        {
            return Fail("Файл слишком большой для резервной копии настроек");
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Fail("Это не файл резервной копии: содержимое не читается как JSON");
        }

        if (parsed is not JsonObject root || root["app"]?.GetValue<string>() != AppMarker)
        {
            return Fail("Это не резервная копия настроек IpGuard");
        }

        if (!TryInt(root["formatVersion"], out var version) || version < 1 || version > FormatVersion)
        {
            return Fail("Файл создан более новой версией программы, обновите IpGuard");
        }

        var notes = new List<string>();
        Settings settings;
        try
        {
            settings = root["settings"] is JsonObject node ? node.Deserialize<Settings>(Options) ?? new Settings() : new Settings();
        }
        catch (JsonException)
        {
            return Fail("Раздел с настройками повреждён");
        }

        settings = Sanitize(settings, notes);
        var references = ReadReferences(root["references"], notes);
        return new BackupResult(new BackupData(settings, references), null, notes);
    }

    public static string Describe(BackupResult result)
    {
        if (result.Data is not { } data)
        {
            return result.Error ?? "";
        }

        var lines = new List<string>
        {
            "Заменить текущие настройки и список эталонов данными из файла?",
            "",
            $"Эталонов в файле: {data.References.Count}."
        };
        if (data.Settings.ProtectEnabled)
        {
            lines.Add(data.Settings.ProtectPrograms.Count > 0
                ? $"Файл включает защиту: при тревоге будут закрываться программы: {string.Join(", ", data.Settings.ProtectPrograms)}."
                : "Файл включает защиту, но список программ в нём пуст.");
        }

        if (result.Notes.Count > 0)
        {
            lines.Add("");
            lines.Add("Что пришлось исправить:");
            lines.AddRange(result.Notes.Distinct().Take(6).Select(note => "• " + note));
            if (result.Notes.Distinct().Count() > 6)
            {
                lines.Add($"• и ещё {result.Notes.Distinct().Count() - 6}");
            }
        }

        lines.Add("");
        lines.Add("Текущие настройки будут перезаписаны.");
        return string.Join(Environment.NewLine, lines);
    }

    public static Settings Sanitize(Settings source, List<string> notes)
    {
        var result = new Settings();
        result.CopyFrom(source);
        result.LastUpdateCheckUtc = null;
        result.NotifiedUpdate = "";

        var country = (source.HomeCountryCode ?? "").Trim().ToUpperInvariant();
        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            notes.Add("Домашняя страна не распознана, оставлена RU");
            country = "RU";
        }

        result.HomeCountryCode = country;
        result.IntervalSeconds = Clamp(source.IntervalSeconds, SettingsForm.MinInterval, SettingsForm.MaxInterval, 15, "Интервал проверки", notes);
        result.SoundVolume = Clamp(source.SoundVolume, 0, 100, 100, "Громкость", notes);
        result.ProtectOutageSeconds = Clamp(source.ProtectOutageSeconds, 5, 3600, 30, "Время потери связи для защиты", notes);
        result.ProtectGraceSeconds = Clamp(source.ProtectGraceSeconds, 0, 120, 10, "Время на отмену", notes);

        if (!OutageChoices.Contains(source.OutageSeconds))
        {
            notes.Add("Задержка уведомления о потере связи заменена на 30 секунд");
            result.OutageSeconds = 30;
        }

        if (source.StatusPort is < SettingsForm.MinPort or > SettingsForm.MaxPort)
        {
            notes.Add("Порт для скриптов вне допустимого диапазона, возвращён 47831");
            result.StatusPort = new Settings().StatusPort;
        }

        var kinds = Sounds.Kinds.Select(kind => kind.Key).ToHashSet();
        result.Sounds = new Dictionary<string, int>();
        foreach (var (key, variant) in source.Sounds ?? new Dictionary<string, int>())
        {
            if (kinds.Contains(key) && variant >= 1 && variant <= Sounds.Variants)
            {
                result.Sounds[key] = variant;
            }
            else
            {
                notes.Add($"Выбор звука «{key}» пропущен");
            }
        }

        result.ProtectPrograms = new List<string>();
        foreach (var raw in (source.ProtectPrograms ?? new List<string>()).Take(MaxPrograms * 2))
        {
            var name = ProgramCloser.Normalize(raw);
            if (name.Length == 0 || name.Length > 64 || name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                notes.Add("Пропущено недопустимое имя программы");
                continue;
            }

            if (ProgramCloser.IsProtected(name))
            {
                notes.Add($"Программа «{name}» пропущена: она нужна системе или самой IpGuard");
                continue;
            }

            if (result.ProtectPrograms.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (result.ProtectPrograms.Count >= MaxPrograms)
            {
                notes.Add($"В списке защиты оставлены первые {MaxPrograms} программ");
                break;
            }

            result.ProtectPrograms.Add(name);
        }

        return result;
    }

    private static List<Reference> ReadReferences(JsonNode? node, List<string> notes)
    {
        var result = new List<Reference>();
        if (node is not JsonArray array)
        {
            return result;
        }

        foreach (var item in array.Take(MaxReferences * 2))
        {
            if (item is not JsonObject entry)
            {
                notes.Add("Пропущен недопустимый эталон");
                continue;
            }

            var country = (Text(entry, "country") ?? "").Trim().ToUpperInvariant();
            var ip = (Text(entry, "ip") ?? "").Trim();
            var label = (Text(entry, "label") ?? "").Trim();
            if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper) || !IPAddress.TryParse(ip, out var address))
            {
                notes.Add("Пропущен недопустимый эталон");
                continue;
            }

            if (label.Length > MaxLabel)
            {
                label = label[..MaxLabel];
            }

            var normalized = address.ToString();
            if (result.Any(existing => existing.Country == country && existing.Ip == normalized))
            {
                continue;
            }

            if (result.Count >= MaxReferences)
            {
                notes.Add($"Оставлены первые {MaxReferences} эталонов");
                break;
            }

            result.Add(new Reference(country, normalized, label));
        }

        return result;
    }

    private static int Clamp(int value, int min, int max, int fallback, string name, List<string> notes)
    {
        if (value >= min && value <= max)
        {
            return value;
        }

        notes.Add($"{name}: значение вне допустимых границ, возвращено {fallback}");
        return fallback;
    }

    private static bool TryInt(JsonNode? node, out int value)
    {
        value = 0;
        try
        {
            if (node is JsonValue json && json.TryGetValue(out int number))
            {
                value = number;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static string? Text(JsonObject entry, string name)
    {
        foreach (var (key, node) in entry)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && node is JsonValue value && value.TryGetValue(out string? text))
            {
                return text;
            }
        }

        return null;
    }

    private static BackupResult Fail(string message) => new(null, message, Array.Empty<string>());
}
