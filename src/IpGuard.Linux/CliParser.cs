namespace IpGuard;

public enum CliKind
{
    Run,
    Status,
    Watch,
    Trust,
    Home,
    Version,
    Help,
    Unknown
}

public sealed record CliCommand(CliKind Kind, string? Argument = null);

public static class CliParser
{
    public const string OnceFlag = "--once";

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliCommand(CliKind.Run);
        }

        var rest = args.Skip(1).ToArray();
        return args[0] switch
        {
            "run" when rest.Length == 0 => new CliCommand(CliKind.Run),
            "status" when rest.Length == 0 => new CliCommand(CliKind.Status),
            "watch" when rest.Length == 0 => new CliCommand(CliKind.Watch),
            "watch" when rest is [OnceFlag] => new CliCommand(CliKind.Watch, OnceFlag),
            "trust" => new CliCommand(CliKind.Trust, string.Join(' ', rest).Trim()),
            "home" => new CliCommand(CliKind.Home, rest.Length == 1 ? rest[0] : null),
            "version" or "--version" when rest.Length == 0 => new CliCommand(CliKind.Version),
            "help" or "-h" or "--help" when rest.Length == 0 => new CliCommand(CliKind.Help),
            _ => new CliCommand(CliKind.Unknown, args[0])
        };
    }
}
