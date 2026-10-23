using System.Security.Cryptography;
using System.Text;

namespace IpGuard;

public static class DataFolder
{
    public const string Variable = "IPGUARD_DATA_DIR";

    private const string BaseMutexName = @"Local\IpGuard.SingleInstance";

    public static string Default() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IpGuard");

    public static string Resolve(Func<string, string?> environment, string defaultPath)
    {
        var custom = environment(Variable);
        return !string.IsNullOrWhiteSpace(custom) && Path.IsPathFullyQualified(custom)
            ? Path.GetFullPath(custom)
            : defaultPath;
    }

    public static string Current() => Resolve(Environment.GetEnvironmentVariable, Default());

    public static string MutexName(string current, string defaultPath)
    {
        if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
        {
            return BaseMutexName;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(current).ToLowerInvariant()));
        return $"{BaseMutexName}.{Convert.ToHexString(hash)[..16]}";
    }

    public static string MutexName() => MutexName(Current(), Default());
}
