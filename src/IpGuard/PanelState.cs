namespace IpGuard;

public enum Tone
{
    Calm,
    Alert,
    Offline
}

public sealed record PanelState(Status Status, string Label, string Ip, string Country, string Provider, string Note)
{
    public static PanelState Initial { get; } = new(Status.Unavailable, "Проверка...", "", "", "", "");

    public static PanelState From(Verdict verdict, IpInfo? info, string note)
    {
        var label = Labels.For(verdict.Status);
        return info is null
            ? new PanelState(verdict.Status, label, "", "", "", "")
            : new PanelState(verdict.Status, label, info.Ip, info.Country, info.Provider, note);
    }

    public static Tone ToneOf(Status status) => status switch
    {
        Status.Changed or Status.Leak or Status.VpnOff => Tone.Alert,
        Status.Unavailable => Tone.Offline,
        _ => Tone.Calm
    };

    public static string Fit(string text, int max)
    {
        if (max <= 0)
        {
            return "";
        }

        if (text.Length <= max)
        {
            return text;
        }

        return max == 1 ? "…" : text[..(max - 1)].TrimEnd() + "…";
    }
}
