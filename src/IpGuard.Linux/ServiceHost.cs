namespace IpGuard;

public static class ServiceHost
{
    public static Daemon CreateDaemon(DataPaths paths, string version)
    {
        var primary = new IpLookup();
        var daemon = new Daemon(new DaemonOptions(
            paths,
            primary,
            () => primary.PausedSources,
            IpLookup.Direct(),
            new Ipv6Lookup(),
            IpLookup.SystemProxyActive,
            NotifierFactory.Create,
            version));
        LogCrashes(new Journal(paths.Log));
        return daemon;
    }

    private static void LogCrashes(Journal journal)
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            journal.Write(ErrorText.Format("фоновая задача", e.Exception));
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => journal.Write(ErrorText.Format("необработанная ошибка", e.ExceptionObject as Exception));
    }
}
