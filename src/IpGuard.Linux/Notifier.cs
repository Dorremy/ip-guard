using System.ComponentModel;
using System.Diagnostics;

namespace IpGuard;

public interface INotifier
{
    Task SendAsync(string title, string body, bool critical);
}

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellation);
}

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Процесс не запустился");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(Limit);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }

            return -1;
        }
    }
}

public sealed class NotifySendNotifier : INotifier
{
    public const string Command = "notify-send";

    private readonly IProcessRunner runner;
    private readonly Action<string> report;
    private bool reported;

    public NotifySendNotifier(IProcessRunner runner, Action<string> report)
    {
        this.runner = runner;
        this.report = report;
    }

    public static IReadOnlyList<string> BuildArguments(string title, string body, bool critical) =>
        new[] { "-a", "IpGuard", "-u", critical ? "critical" : "normal", "--", title, body };

    public async Task SendAsync(string title, string body, bool critical)
    {
        try
        {
            var code = await runner.RunAsync(Command, BuildArguments(title, body, critical), CancellationToken.None);
            if (code != 0)
            {
                ReportOnce($"Уведомления: notify-send завершился с кодом {code}");
            }
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            ReportOnce("Уведомления недоступны: не найден notify-send (пакет libnotify-bin)");
        }
    }

    private void ReportOnce(string message)
    {
        if (reported)
        {
            return;
        }

        reported = true;
        report(message);
    }
}

public sealed class NullNotifier : INotifier
{
    public Task SendAsync(string title, string body, bool critical) => Task.CompletedTask;
}

public static class NotifierFactory
{
    public static INotifier Create(Action<string> report) =>
        OperatingSystem.IsWindows() ? new NullNotifier() : new NotifySendNotifier(new ProcessRunner(), report);
}
