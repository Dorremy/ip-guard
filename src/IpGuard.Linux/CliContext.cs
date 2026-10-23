namespace IpGuard;

public sealed record CliContext(
    DataPaths Paths,
    TextWriter Out,
    TextWriter Err,
    Func<DateTime> UtcNow,
    Func<IIpLookup> Lookup,
    string Version);
