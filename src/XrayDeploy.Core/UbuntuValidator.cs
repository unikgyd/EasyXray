namespace XrayDeploy.Core;

public sealed record UbuntuValidationResult(bool IsSupported, string? DistributionId, string? Architecture, string? FailureReason)
{
    public static UbuntuValidationResult Unsupported(string reason) => new(false, null, null, reason);
}

public sealed class UbuntuValidator
{
    private static readonly HashSet<string> SupportedArchitectures = new(StringComparer.Ordinal) { "x86_64", "aarch64" };

    public async Task<UbuntuValidationResult> ValidateAsync(IRemoteShell shell, CancellationToken cancellationToken = default)
    {
        var osRelease = await shell.ExecuteAsync("cat /etc/os-release", cancellationToken);
        if (!osRelease.Succeeded)
            return UbuntuValidationResult.Unsupported("Unable to read /etc/os-release.");

        var id = ParseValue(osRelease.StandardOutput, "ID");
        if (!string.Equals(id, "ubuntu", StringComparison.Ordinal))
            return new(false, id, null, "Only Ubuntu servers are supported.");

        var architecture = await shell.ExecuteAsync("uname -m", cancellationToken);
        if (!architecture.Succeeded)
            return new(false, id, null, "Unable to determine the server architecture.");

        var normalizedArchitecture = architecture.StandardOutput.Trim();
        return SupportedArchitectures.Contains(normalizedArchitecture)
            ? new(true, id, normalizedArchitecture, null)
            : new(false, id, normalizedArchitecture, "Only x86_64 and aarch64 Ubuntu servers are supported.");
    }

    private static string? ParseValue(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || !string.Equals(line[..separator], key, StringComparison.Ordinal)) continue;
            return line[(separator + 1)..].Trim().Trim('"', '\'', '\r');
        }
        return null;
    }
}
