using System.Text;
using System.Text.RegularExpressions;
using XrayDeploy.Core;

namespace XrayDeploy.Infrastructure;

public sealed class XrayInstaller(IRemoteRootContext context)
{
    private const string XrayExecutable = "/usr/local/bin/xray";
    private const string InstallerUrl = "https://github.com/XTLS/Xray-install/raw/main/install-release.sh";

    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        var installed = await context.RootShell.ExecuteAsync($"test -x {PosixShellEscaper.Quote(XrayExecutable)}", cancellationToken);
        if (installed.Succeeded) return;

        var command = "apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y ca-certificates curl " +
                      $"&& curl -fsSL {PosixShellEscaper.Quote(InstallerUrl)} -o /tmp/xraydeploy-xray-install.sh " +
                      "&& bash /tmp/xraydeploy-xray-install.sh install && rm -f /tmp/xraydeploy-xray-install.sh";
        var result = await context.RootShell.ExecuteAsync(command, cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Xray installation failed: {result.StandardError}");

        var verify = await context.RootShell.ExecuteAsync($"test -x {PosixShellEscaper.Quote(XrayExecutable)} && systemctl cat xray.service >/dev/null", cancellationToken);
        if (!verify.Succeeded)
            throw new InvalidOperationException("Xray installer completed but the xray executable or systemd service is unavailable.");
    }
}

public sealed class XrayServiceHealthChecker(IRemoteRootContext context) : IXrayServiceHealthChecker
{
    public async Task<XrayServiceStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var loadState = await context.RootShell.ExecuteAsync("systemctl show xray.service --property=LoadState --value", cancellationToken);
        if (!loadState.Succeeded || !string.Equals(loadState.StandardOutput.Trim(), "loaded", StringComparison.OrdinalIgnoreCase))
            return new(false, false, "未检测到 Xray systemd 服务。部署时会自动安装 Xray。");

        var state = await context.RootShell.ExecuteAsync("systemctl is-active --quiet xray.service", cancellationToken);
        if (state.Succeeded)
            return new(true, true, "Xray 服务运行正常（systemd: active）。");

        var details = await context.RootShell.ExecuteAsync("journalctl -u xray.service -n 8 --no-pager -o cat", cancellationToken);
        var lastLine = details.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.Contains("Failed", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase));
        return new(true, false, string.IsNullOrWhiteSpace(lastLine)
            ? "Xray 服务未运行；请查看 systemd 日志。"
            : $"Xray 服务未运行：{lastLine}");
    }
}

public sealed class RemoteRealityKeyGenerator(IRemoteRootContext context, XrayInstaller installer) : IRealityKeyGenerator
{
    private static readonly Regex PrivateKeyPattern = new("^(?:Private key|PrivateKey):\\s*(?<key>\\S+)\\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex PublicKeyPattern = new("^(?:Public key|Password \\(PublicKey\\)):\\s*(?<key>\\S+)\\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public async Task<RealityKeyPair> GenerateAsync(CancellationToken cancellationToken = default)
    {
        await installer.EnsureInstalledAsync(cancellationToken);
        var result = await context.RootShell.ExecuteAsync("/usr/local/bin/xray x25519", cancellationToken);
        var output = result.StandardOutput + "\n" + result.StandardError;
        var privateKey = PrivateKeyPattern.Match(output).Groups["key"].Value;
        var publicKey = PublicKeyPattern.Match(output).Groups["key"].Value;
        if (!result.Succeeded || string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(publicKey))
            throw new InvalidOperationException($"Xray did not return a valid REALITY key pair: {output.Trim()}");
        return new(privateKey, publicKey);
    }
}

public sealed class RealityRemoteDeploymentService(
    IRemoteRootContext context,
    XrayInstaller installer,
    RealityConfigCompiler? compiler = null) : IRealityDeploymentService
{
    private const string ConfigPath = "/usr/local/etc/xray/config.json";
    private readonly RealityConfigCompiler _compiler = compiler ?? new RealityConfigCompiler();

    public async Task<RealityDeploymentResult> DeployAsync(ServerProfile server, CancellationToken cancellationToken = default)
    {
        var config = _compiler.Compile(server);
        await installer.EnsureInstalledAsync(cancellationToken);
        var serviceGroup = await ResolveServicePrimaryGroupAsync(cancellationToken);

        try
        {
            await StageManagedTlsAsync(server, cancellationToken);
            await using var source = new MemoryStream(Encoding.UTF8.GetBytes(config));
            var staged = await context.FileStager.StageAsync(source, "config.json", cancellationToken);
            var validation = await context.RootShell.ExecuteAsync($"/usr/local/bin/xray run -test -c {PosixShellEscaper.Quote(staged.RemotePath)}", cancellationToken);
            if (!validation.Succeeded)
                return new(false, false, $"Remote Xray configuration validation failed: {validation.StandardError.Trim()}");

            var token = Guid.NewGuid().ToString("N");
            var backupPath = $"{ConfigPath}.backup-{token}";
            var pendingPath = $"{ConfigPath}.pending-{token}";
            var existing = await context.RootShell.ExecuteAsync($"test -f {PosixShellEscaper.Quote(ConfigPath)}", cancellationToken);
            var install = await context.RootShell.ExecuteAsync(
                $"install -d -m 0755 {PosixShellEscaper.Quote("/usr/local/etc/xray")} && " +
                $"if test -f {PosixShellEscaper.Quote(ConfigPath)}; then cp -a {PosixShellEscaper.Quote(ConfigPath)} {PosixShellEscaper.Quote(backupPath)}; fi && " +
                $"install -m 0640 {PosixShellEscaper.Quote(staged.RemotePath)} {PosixShellEscaper.Quote(pendingPath)} && " +
                $"chown root:{PosixShellEscaper.Quote(serviceGroup)} {PosixShellEscaper.Quote(pendingPath)} && " +
                $"mv -f {PosixShellEscaper.Quote(pendingPath)} {PosixShellEscaper.Quote(ConfigPath)} && systemctl restart xray.service && systemctl is-active --quiet xray.service",
                cancellationToken);
            if (install.Succeeded)
                return new(true, false, "Xray configuration deployed and the xray service is active.");

            if (!existing.Succeeded)
            {
                await context.RootShell.ExecuteAsync($"rm -f {PosixShellEscaper.Quote(ConfigPath)} {PosixShellEscaper.Quote(pendingPath)}", cancellationToken);
                return new(false, false, $"Deployment failed before Xray could be started: {install.StandardError.Trim()}");
            }

            var rollback = await context.RootShell.ExecuteAsync(
                $"if test -f {PosixShellEscaper.Quote(backupPath)}; then cp -a {PosixShellEscaper.Quote(backupPath)} {PosixShellEscaper.Quote(pendingPath)} && chown root:{PosixShellEscaper.Quote(serviceGroup)} {PosixShellEscaper.Quote(pendingPath)} && chmod 0640 {PosixShellEscaper.Quote(pendingPath)} && mv -f {PosixShellEscaper.Quote(pendingPath)} {PosixShellEscaper.Quote(ConfigPath)} && systemctl restart xray.service && systemctl is-active --quiet xray.service; else false; fi",
                cancellationToken);
            return rollback.Succeeded
                ? new(false, true, $"Deployment failed and the previous configuration was restored: {install.StandardError.Trim()}")
                : new(false, false, $"Deployment failed and rollback could not restore a healthy xray service: {rollback.StandardError.Trim()}");
        }
        finally
        {
            await context.FileStager.CleanupAsync(cancellationToken);
        }
    }

    private async Task<string> ResolveServicePrimaryGroupAsync(CancellationToken cancellationToken)
    {
        var serviceUser = await context.RootShell.ExecuteAsync("systemctl show xray.service --property=User --value", cancellationToken);
        var user = serviceUser.StandardOutput.Trim();
        if (!serviceUser.Succeeded || string.IsNullOrWhiteSpace(user)) return "root";

        var serviceGroup = await context.RootShell.ExecuteAsync("systemctl show xray.service --property=Group --value", cancellationToken);
        var group = serviceGroup.StandardOutput.Trim();
        if (serviceGroup.Succeeded && !string.IsNullOrWhiteSpace(group)) return group;

        var primaryGroup = await context.RootShell.ExecuteAsync($"id -gn {PosixShellEscaper.Quote(user)}", cancellationToken);
        if (!primaryGroup.Succeeded || string.IsNullOrWhiteSpace(primaryGroup.StandardOutput))
            throw new InvalidOperationException($"Unable to resolve the primary group for the Xray service account '{user}'.");
        return primaryGroup.StandardOutput.Trim();
    }

    private async Task StageManagedTlsAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        foreach (var inbound in server.Inbounds.OfType<Hysteria2InboundProfile>().Where(profile => profile.Enabled && (!string.IsNullOrWhiteSpace(profile.TlsCertificatePem) || !string.IsNullOrWhiteSpace(profile.TlsCertificateLocalPath))))
        {
            await using Stream certificate = !string.IsNullOrWhiteSpace(inbound.TlsCertificatePem) ? new MemoryStream(Encoding.UTF8.GetBytes(inbound.TlsCertificatePem!)) : File.OpenRead(inbound.TlsCertificateLocalPath!);
            await using Stream privateKey = !string.IsNullOrWhiteSpace(inbound.TlsPrivateKeyPem) ? new MemoryStream(Encoding.UTF8.GetBytes(inbound.TlsPrivateKeyPem!)) : File.OpenRead(inbound.TlsPrivateKeyLocalPath!);
            var stagedCertificate = await context.FileStager.StageAsync(certificate, $"{inbound.Id:N}.crt", cancellationToken);
            var stagedPrivateKey = await context.FileStager.StageAsync(privateKey, $"{inbound.Id:N}.key", cancellationToken);
            await context.FileStager.InstallAsync(stagedCertificate, HysteriaTlsPaths.Certificate(inbound), "0644", cancellationToken);
            await context.FileStager.InstallAsync(stagedPrivateKey, HysteriaTlsPaths.PrivateKey(inbound), "0600", cancellationToken);
        }
    }
}
