using System.Media;
using System.Reflection;

namespace IpGuard;

public static class Sounds
{
    public const int Variants = SoundCatalog.Variants;

    public static readonly (string Key, string Title, string[] Names)[] Kinds = SoundCatalog.Kinds;

    private static SoundPlayer? player;
    private static Stream? stream;

    public static int Volume { get; set; } = 100;

    public static string? KeyFor(Status status) => SoundCatalog.KeyFor(status);

    public static string ResourceName(string key, int variant) => $"sounds.{key}-{variant}.wav";

    public static byte[]? Load(string key, int variant)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(key, variant));
        if (source is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        source.CopyTo(copy);
        return copy.ToArray();
    }

    public static byte[] Scale(byte[] wav, int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        if (volume == 100 || wav.Length < 44 || !IsPcm16(wav))
        {
            return wav;
        }

        var gain = Math.Pow(volume / 100.0, 2);
        var result = (byte[])wav.Clone();
        var position = 12;
        while (position + 8 <= result.Length)
        {
            var size = BitConverter.ToInt32(result, position + 4);
            var isData = result[position] == 'd' && result[position + 1] == 'a' && result[position + 2] == 't' && result[position + 3] == 'a';
            var start = position + 8;
            if (isData)
            {
                var end = size < 0 || start + (long)size > result.Length ? result.Length : start + size;
                for (var i = start; i + 1 < end; i += 2)
                {
                    var sample = BitConverter.ToInt16(result, i);
                    var scaled = (short)Math.Round(sample * gain);
                    result[i] = (byte)(scaled & 0xFF);
                    result[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }

                break;
            }

            position = start + size + (size & 1);
        }

        return result;
    }

    public static void Play(string key, int variant, int? volume = null)
    {
        var bytes = Load(key, variant);
        if (bytes is null)
        {
            return;
        }

        var next = new MemoryStream(Scale(bytes, volume ?? Volume));
        player?.Stop();
        player?.Dispose();
        stream?.Dispose();
        stream = next;
        player = new SoundPlayer(stream);
        player.Play();
    }

    private static bool IsPcm16(byte[] wav)
    {
        if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
        {
            return false;
        }

        var position = 12;
        while (position + 8 <= wav.Length)
        {
            var size = BitConverter.ToInt32(wav, position + 4);
            if (wav[position] == 'f' && wav[position + 1] == 'm' && wav[position + 2] == 't' && wav[position + 3] == ' ')
            {
                return position + 22 <= wav.Length
                    && BitConverter.ToInt16(wav, position + 8) == 1
                    && BitConverter.ToInt16(wav, position + 22) == 16;
            }

            position += 8 + size + (size & 1);
        }

        return false;
    }
}
