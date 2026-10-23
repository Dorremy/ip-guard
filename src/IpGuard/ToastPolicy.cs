using System.Runtime.InteropServices;

namespace IpGuard;

public static class ToastPolicy
{
    public const int QunsNotPresent = 1;
    public const int QunsBusy = 2;
    public const int QunsFullScreen = 3;
    public const int QunsPresentation = 4;
    public const int QunsAcceptsNotifications = 5;
    public const int QunsQuietTime = 6;
    public const int QunsApp = 7;
    public const int TitleMax = 44;
    public const int MessageMax = 150;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    public static bool SystemShouldDecide(int state) =>
        state is QunsBusy or QunsFullScreen or QunsPresentation or QunsQuietTime;

    public static bool SystemShouldDecideNow()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 && SystemShouldDecide(state);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            return false;
        }
    }

    public static float HoldFor(Tone tone) => tone switch
    {
        Tone.Alert => 9f,
        Tone.Offline => 7f,
        _ => 5f
    };

    public static (string Title, string Message) Fit(string? title, string? message) =>
        (Trim(title, TitleMax), Trim(message, MessageMax));

    private static string Trim(string? text, int max)
    {
        var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }
}
