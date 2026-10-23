namespace IpGuard;

public static class HomeCommand
{
    public static bool IsValid(string? code) =>
        code is { Length: 2 } && code.All(char.IsAsciiLetter);

    public static int Run(CliContext context, string? code)
    {
        if (!IsValid(code))
        {
            context.Err.WriteLine("Код страны — ровно две латинские буквы, например: ipguard home RU");
            return 2;
        }

        var normalized = code!.ToUpperInvariant();
        var store = context.Paths.SettingsStore();
        var settings = store.Load();
        settings.HomeCountryCode = normalized;
        try
        {
            store.Save(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Err.WriteLine($"Не удалось сохранить настройки: {e.Message}");
            return 1;
        }

        new Journal(context.Paths.Log).Write($"Домашняя страна: {normalized}");
        context.Out.WriteLine($"Домашняя страна: {Countries.Describe(normalized)}");
        return 0;
    }
}
