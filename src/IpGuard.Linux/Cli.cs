namespace IpGuard;

public static class Cli
{
    public const string HelpText =
        "IpGuard: следит за внешним IP и сообщает, если VPN отвалился.\n" +
        "\n" +
        "Использование: ipguard [команда]\n" +
        "\n" +
        "  run              запустить службу (по умолчанию)\n" +
        "  status           показать текущее состояние\n" +
        "  watch [--once]   живой экран состояния; с --once — один кадр и выход\n" +
        "  trust [подпись]  запомнить текущий IP как эталон для его страны\n" +
        "  home XX          задать домашнюю страну двухбуквенным кодом\n" +
        "  version          показать версию\n" +
        "  help             показать эту справку";

    public static async Task<int> ExecuteAsync(string[] args, CliContext context, Func<Task<int>> run, Func<bool, Task<int>>? watch = null)
    {
        var command = CliParser.Parse(args);
        switch (command.Kind)
        {
            case CliKind.Run:
                return await run();
            case CliKind.Status:
                return StatusCommand.Run(context);
            case CliKind.Watch:
                var once = command.Argument == CliParser.OnceFlag;
                return watch is null ? WatchCommand.RunOnce(context) : await watch(once);
            case CliKind.Trust:
                return await TrustCommand.RunAsync(context, command.Argument ?? "");
            case CliKind.Home:
                return HomeCommand.Run(context, command.Argument);
            case CliKind.Version:
                context.Out.WriteLine($"ipguard {context.Version}");
                return 0;
            case CliKind.Help:
                context.Out.WriteLine(HelpText);
                return 0;
            default:
                context.Err.WriteLine($"Неизвестная команда: {command.Argument}");
                context.Err.WriteLine();
                context.Err.WriteLine(HelpText);
                return 2;
        }
    }
}
