namespace IpGuard;

public sealed record DataPaths(string Directory)
{
    public string Settings => Path.Combine(Directory, "settings.json");

    public string State => Path.Combine(Directory, "state.json");

    public string Log => Path.Combine(Directory, "log.txt");

    public string Status => Path.Combine(Directory, "status.json");

    public string Backups => Path.Combine(Directory, "backups");

    public JsonStore<Settings> SettingsStore() => new(Settings, Backups);

    public JsonStore<MonitorState> StateStore() => new(State, Backups);
}
