using System.Runtime.InteropServices;

namespace IpGuard;

public static class WatchCommand
{
    private const int OnceHistoryRows = 10;
    private const int OnceJournalRows = 5;
    private const int LoadedHistoryRows = 200;
    private const int LoadedJournalRows = 6;
    private const int OnceWidth = 100;
    private const int OnceHeight = 1000;
    private static readonly TimeSpan Redraw = TimeSpan.FromSeconds(1);

    public static int RunOnce(CliContext context)
    {
        var windows = OperatingSystem.IsWindows();
        var mode = ProbeMode();
        var width = OnceWidth;
        if (!Console.IsOutputRedirected && ReferenceEquals(context.Out, Console.Out))
        {
            try
            {
                width = Math.Max(20, Console.WindowWidth);
            }
            catch (IOException)
            {
            }
        }

        var model = WatchModel.Load(context.Paths, context.UtcNow(), mode, windows, false, null, OnceHistoryRows, OnceJournalRows);
        foreach (var line in WatchScreen.Render(model, width, OnceHeight, false))
        {
            context.Out.WriteLine(line);
        }

        return model.Healthy ? 0 : 1;
    }

    public static async Task<int> RunAsync(CliContext context, bool once, Func<Daemon> createDaemon)
    {
        if (once || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return RunOnce(context);
        }

        using var instance = InstanceLock.TryAcquire(DataFolder.MutexName());
        var mode = instance is null ? WatchMode.Client : WatchMode.Own;
        Daemon? daemon = null;
        var startedUtc = context.UtcNow();
        try
        {
            if (mode == WatchMode.Own)
            {
                daemon = createDaemon();
                _ = daemon.Start();
            }

            await LoopAsync(context, mode, daemon, startedUtc);
        }
        finally
        {
            if (daemon is not null)
            {
                context.Out.WriteLine("Останавливаю мониторинг…");
                await daemon.StopAsync();
                daemon.Dispose();
            }
        }

        return 0;
    }

    private static WatchMode ProbeMode()
    {
        using var instance = InstanceLock.TryAcquire(DataFolder.MutexName());
        return instance is null ? WatchMode.Client : WatchMode.Idle;
    }

    private static async Task LoopAsync(CliContext context, WatchMode mode, Daemon? daemon, DateTime startedUtc)
    {
        using var stop = new CancellationTokenSource();
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal => Stop(signal, stop));
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, signal => Stop(signal, stop));
        using var hangup = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, signal => Stop(signal, stop));

        var windows = OperatingSystem.IsWindows();
        var controller = new WatchController(WatchCaps.For(mode, windows), new WatchActions(context, daemon), context.UtcNow);
        var colorWanted = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        var stamps = new[] { new FileStamp(context.Paths.Status), new FileStamp(context.Paths.State), new FileStamp(context.Paths.Log) };

        using var terminal = Terminal.Open();
        var color = colorWanted && terminal.Color;
        WatchModel? loaded = null;
        var lastDraw = DateTime.MinValue;
        var lastSize = (0, 0);
        string? lastPrompt = null;
        string? lastNotice = null;
        var dirty = true;

        while (!stop.IsCancellationRequested)
        {
            while (Console.KeyAvailable)
            {
                var result = controller.Handle(Console.ReadKey(true));
                if (result == WatchKeyResult.Quit)
                {
                    return;
                }

                dirty |= result == WatchKeyResult.Redraw;
            }

            var reload = loaded is null || stamps.Any(stamp => stamp.Changed());
            if (reload)
            {
                loaded = WatchModel.Load(context.Paths, context.UtcNow(), mode, windows, true, mode == WatchMode.Own ? startedUtc : null, LoadedHistoryRows, LoadedJournalRows);
                foreach (var stamp in stamps)
                {
                    stamp.Mark();
                }
            }

            var size = terminal.Size();
            var now = DateTime.UtcNow;
            var prompt = controller.Prompt;
            var notice = controller.Notice;
            if (dirty || reload || size != lastSize || prompt != lastPrompt || notice != lastNotice || now - lastDraw >= Redraw)
            {
                var model = loaded! with { NowUtc = context.UtcNow(), Prompt = prompt, Notice = notice };
                terminal.Draw(WatchScreen.Render(model, size.Width, size.Height, color), size.Width, size.Height);
                lastDraw = now;
                lastSize = size;
                lastPrompt = prompt;
                lastNotice = notice;
                dirty = false;
            }

            try
            {
                await Task.Delay(100, stop.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static void Stop(PosixSignalContext signal, CancellationTokenSource stop)
    {
        signal.Cancel = true;
        stop.Cancel();
    }
}
