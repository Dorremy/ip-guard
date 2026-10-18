using System.Reflection;

namespace IpGuard;

public static class TrayIcons
{
    private static readonly Dictionary<Status, Icon> Cache = new();

    public static Icon For(Status status)
    {
        if (!Cache.TryGetValue(status, out var icon))
        {
            icon = Load(NameOf(status));
            Cache[status] = icon;
        }

        return icon;
    }

    private static string NameOf(Status status) => status switch
    {
        Status.Ready or Status.Unchanged => "ready",
        Status.Changed => "changed",
        Status.VpnOff => "vpnoff",
        Status.Home => "home",
        _ => "offline"
    };

    private static Icon Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"icons.{name}.ico")
            ?? throw new InvalidOperationException($"Missing icon resource: {name}");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
