using System.Text.Json;
using System.Text.Json.Nodes;

namespace IpGuard;

public sealed record StatusSnapshot(
    bool Ok,
    string State,
    string Title,
    string Message,
    string? Ip,
    string? CountryCode,
    string? Country,
    string? Provider,
    IReadOnlyList<string> References,
    string? Leak,
    DateTime UpdatedUtc,
    string Version);

public static class StatusMapper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static string Key(Status status) => status switch
    {
        Status.Unchanged or Status.Ready => "ok",
        Status.NoReference => "no_reference",
        Status.Changed => "changed",
        Status.Leak => "leak",
        Status.VpnOff => "vpn_off",
        Status.Home => "home",
        _ => "offline"
    };

    public static StatusSnapshot Build(
        Verdict verdict,
        IpInfo? info,
        IReadOnlyList<Reference> references,
        string? leak,
        DateTime nowUtc,
        string version) =>
        new(
            verdict.Status is Status.Unchanged or Status.Ready,
            Key(verdict.Status),
            verdict.Title,
            verdict.Message,
            info?.Ip,
            info?.CountryCode,
            info?.Country,
            info?.Provider,
            references.Select(reference => reference.Label.Length > 0 ? $"{reference.Ip} ({reference.Label})" : reference.Ip).ToList(),
            leak,
            nowUtc,
            version);

    public static string ToJson(StatusSnapshot snapshot, DateTime? nowUtc = null)
    {
        var node = JsonSerializer.SerializeToNode(snapshot, Options)!.AsObject();
        if (nowUtc is { } now)
        {
            node["ageSeconds"] = Math.Max(0, (int)(now - snapshot.UpdatedUtc).TotalSeconds);
        }

        return node.ToJsonString(Options);
    }

    public static void WriteFile(string path, StatusSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(snapshot), new System.Text.UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
