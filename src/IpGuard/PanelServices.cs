namespace IpGuard;

public sealed class PanelServices
{
    public Func<Settings> GetSettings { get; init; } = () => new Settings();

    public Func<bool> GetAutostart { get; init; } = () => false;

    public Action<Settings, bool> ApplySettings { get; init; } = (_, _) => { };

    public Func<MonitorState> GetState { get; init; } = () => new MonitorState();

    public Action ReferencesChanged { get; init; } = () => { };

    public Func<IpInfo?> GetCurrentIp { get; init; } = () => null;

    public Func<string> GetHome { get; init; } = () => "RU";

    public Func<IReadOnlyList<RunningProgram>> GetPrograms { get; init; } = () => Array.Empty<RunningProgram>();

    public Action<string, int, int> PlaySound { get; init; } = (_, _, _) => { };
}
