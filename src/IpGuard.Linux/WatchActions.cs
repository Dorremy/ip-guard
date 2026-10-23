namespace IpGuard;

public sealed class WatchActions : IWatchActions
{
    private readonly CliContext context;
    private readonly Daemon? daemon;

    public WatchActions(CliContext context, Daemon? daemon)
    {
        this.context = context;
        this.daemon = daemon;
    }

    public async Task<string> TrustAsync()
    {
        var (inner, output, error) = Capture();
        await TrustCommand.RunAsync(inner, "");
        await Refresh();
        return Text(output, error);
    }

    public async Task<string> SetHomeAsync(string code)
    {
        var (inner, output, error) = Capture();
        HomeCommand.Run(inner, code);
        await Refresh();
        return Text(output, error);
    }

    public Task CheckNowAsync() => daemon is null ? Task.CompletedTask : daemon.CheckAsync(true, 2, "вручную");

    private (CliContext Context, StringWriter Out, StringWriter Err) Capture()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (context with { Out = output, Err = error }, output, error);
    }

    private Task Refresh() => daemon is null ? Task.CompletedTask : daemon.CheckAsync(false, 2, "команда");

    private static string Text(StringWriter output, StringWriter error)
    {
        var text = (error.ToString() + output.ToString()).Trim();
        return text.Length == 0 ? "Готово" : text.Replace("\r", "").Replace('\n', ' ');
    }
}
