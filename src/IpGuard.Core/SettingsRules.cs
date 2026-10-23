namespace IpGuard;

public static class SettingsRules
{
    public const int MinInterval = 15;
    public const int MaxInterval = 3600;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    public static int ClampInterval(int seconds) => Math.Clamp(seconds, MinInterval, MaxInterval);

    public static int ClampPort(int port) => port is >= MinPort and <= MaxPort ? port : new Settings().StatusPort;

    public static Settings Clone(Settings source)
    {
        var copy = new Settings();
        copy.CopyFrom(source);
        return copy;
    }
}
