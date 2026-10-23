using System.Reflection;
using System.Runtime.InteropServices;

namespace IpGuard;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var paths = new DataPaths(DataFolder.Current());
        var version = ProductVersion();
        var context = new CliContext(paths, Console.Out, Console.Error, () => DateTime.UtcNow, () => new IpLookup(), version);
        return Cli.ExecuteAsync(args, context, () => RunServiceAsync(paths, version)).GetAwaiter().GetResult();
    }

    private static async Task<int> RunServiceAsync(DataPaths paths, string version)
    {
        using var instance = InstanceLock.TryAcquire(DataFolder.MutexName());
        if (instance is null)
        {
            Console.Error.WriteLine("Служба уже запущена");
            return 1;
        }

        using var stop = new CancellationTokenSource();
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Stop(context, stop));
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Stop(context, stop));

        var primary = new IpLookup();
        using var daemon = new Daemon(new DaemonOptions(
            paths,
            primary,
            () => primary.PausedSources,
            IpLookup.Direct(),
            new Ipv6Lookup(),
            IpLookup.SystemProxyActive,
            report => new NotifySendNotifier(new ProcessRunner(), report),
            version));
        var journal = new Journal(paths.Log);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            journal.Write(ErrorText.Format("фоновая задача", e.Exception));
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => journal.Write(ErrorText.Format("необработанная ошибка", e.ExceptionObject as Exception));

        _ = daemon.Start();
        try
        {
            await Task.Delay(Timeout.Infinite, stop.Token);
        }
        catch (OperationCanceledException)
        {
        }

        await daemon.StopAsync();
        return 0;
    }

    private static void Stop(PosixSignalContext context, CancellationTokenSource stop)
    {
        context.Cancel = true;
        stop.Cancel();
    }

    private static string ProductVersion()
    {
        var assembly = typeof(Program).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        return text.Split('+')[0];
    }
}
