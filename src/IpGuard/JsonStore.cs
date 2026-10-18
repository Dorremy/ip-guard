using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpGuard;

public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string path;

    public JsonStore(string path)
    {
        this.path = path;
    }

    public T Load()
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
            }
        }
        catch (JsonException)
        {
            Backup();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return new T();
    }

    private void Backup()
    {
        try
        {
            File.Copy(path, $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bad", true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}
