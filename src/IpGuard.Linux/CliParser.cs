namespace IpGuard;

public enum CliKind
{
    Run,
    Status,
    Trust,
    Home,
    Version,
    Help,
    Unknown
}

public sealed record CliCommand(CliKind Kind, string? Argument = null);

public static class CliParser
{
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
            "trust" => new CliCommand(CliKind.Trust, string.Join(' ', rest).Trim()),
            "home" => new CliCommand(CliKind.Home, rest.Length == 1 ? rest[0] : null),
            "version" or "--version" when rest.Length == 0 => new CliCommand(CliKind.Version),
            "help" or "-h" or "--help" when rest.Length == 0 => new CliCommand(CliKind.Help),
            _ => new CliCommand(CliKind.Unknown, args[0])
        };
    }
}
