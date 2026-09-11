namespace XrayDeploy.Core;

public static class PosixShellEscaper
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    public static string Command(string command) => $"sh -lc {Quote(command)}";
}
