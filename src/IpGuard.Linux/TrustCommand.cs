namespace IpGuard;

public static class TrustCommand
{
    private const int Attempts = 3;

    public static async Task<int> RunAsync(CliContext context, string label)
    {
        var settings = context.Paths.SettingsStore().Load();
        var stateStore = context.Paths.StateStore();
        var state = stateStore.Load();
        state.MigrateLegacy();

        var lookup = context.Lookup();
        IpInfo? info = null;
        for (var i = 0; i < Attempts && info is null; i++)
        {
            info = await lookup.GetAsync();
        }

        if (info is null)
        {
            context.Err.WriteLine("Не удалось определить внешний IP. Проверьте связь и прокси.");
            return 1;
        }

        if (info.CountryCode == settings.HomeCountryCode)
        {
            context.Err.WriteLine($"Сейчас вы в домашней стране ({Countries.Describe(info.CountryCode)}), IP {info.Ip}. Для неё эталон не нужен.");
            return 1;
        }

        var watcher = new IpWatcher(state, () => settings.HomeCountryCode);
        var added = watcher.Trust(info, label);
        try
        {
            stateStore.Save(state);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Err.WriteLine($"Не удалось сохранить эталон: {e.Message}");
            return 1;
        }

        var text = added
            ? $"Эталон добавлен для {info.CountryCode}: {info.Ip}"
            : $"Эталон уже был для {info.CountryCode}: {info.Ip}";
        new Journal(context.Paths.Log).Write(text);
        var note = string.IsNullOrWhiteSpace(label) || !added ? "" : $" ({label.Trim()})";
        context.Out.WriteLine($"{text}{note}");
        return 0;
    }
}
