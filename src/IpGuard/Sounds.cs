using System.Media;
using System.Reflection;

namespace IpGuard;

public static class Sounds
{
    public const int Variants = 5;

    public static readonly (string Key, string Title)[] Kinds =
    {
        ("ready", "Готов к работе"),
        ("changed", "IP отличается от эталона"),
        ("vpnoff", "VPN отключился"),
        ("noref", "Эталон не задан"),
        ("offline", "Нет связи")
    };

    private static SoundPlayer? player;
    private static Stream? stream;

    public static string? KeyFor(Status status) => status switch
    {
        Status.Unchanged => "ready",
        Status.Changed => "changed",
        Status.VpnOff => "vpnoff",
        Status.NoReference => "noref",
        Status.Unavailable => "offline",
        _ => null
    };

    public static string ResourceName(string key, int variant) => $"sounds.{key}-{variant}.wav";

    public static void Play(string key, int variant)
    {
        var next = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(key, variant));
        if (next is null)
        {
            return;
        }

        player?.Stop();
        player?.Dispose();
        stream?.Dispose();
        stream = next;
        player = new SoundPlayer(stream);
        player.Play();
    }
}
