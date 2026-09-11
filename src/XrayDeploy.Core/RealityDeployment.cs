namespace XrayDeploy.Core;

public sealed record RealityKeyPair(string PrivateKey, string PublicKey);

public interface IRealityKeyGenerator
{
    Task<RealityKeyPair> GenerateAsync(CancellationToken cancellationToken = default);
}

public sealed record RealityDeploymentResult(bool Succeeded, bool RolledBack, string Message);

/// <summary>Observable state of the managed Xray systemd service.</summary>
public sealed record XrayServiceStatus(bool IsInstalled, bool IsActive, string Message);

public interface IRealityDeploymentService
{
    Task<RealityDeploymentResult> DeployAsync(ServerProfile server, CancellationToken cancellationToken = default);
}

public interface IXrayServiceHealthChecker
{
    Task<XrayServiceStatus> CheckAsync(CancellationToken cancellationToken = default);
}
