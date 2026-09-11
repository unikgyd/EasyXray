using XrayDeploy.Core;
using XrayDeploy.Infrastructure;

namespace EasyXray.Services;

public sealed record ServerConnectionRequest(
    string Host,
    int Port,
    string UserName,
    string HostFingerprint,
    string? PrivateKeyPath,
    Func<CancellationToken, ValueTask<string?>>? SshPasswordProvider = null,
    Func<CancellationToken, ValueTask<string?>>? PrivateKeyPassphraseProvider = null,
    Func<CancellationToken, ValueTask<string?>>? SudoPasswordProvider = null,
    Func<CancellationToken, ValueTask<string?>>? RootPasswordProvider = null);
public sealed record ServerConnectionResult(bool IsSupported, string Message, string HostFingerprint);

public interface IRemoteWorkflow
{
    Task<ServerConnectionResult> TestConnectionAsync(ServerConnectionRequest request, CancellationToken cancellationToken = default);
    Task<RealityKeyPair> GenerateRealityKeysAsync(ServerConnectionRequest request, CancellationToken cancellationToken = default);
    Task<IssuedCertificate> IssueCertificateAsync(ServerConnectionRequest request, CertificateRequest certificate, CancellationToken cancellationToken = default);
    Task<RealityDeploymentResult> DeployAsync(ServerConnectionRequest request, ServerProfile profile, CancellationToken cancellationToken = default);
    Task<XrayServiceStatus> GetXrayServiceStatusAsync(ServerConnectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class RemoteWorkflow : IRemoteWorkflow
{
    public async Task<ServerConnectionResult> TestConnectionAsync(ServerConnectionRequest request, CancellationToken cancellationToken = default)
    {
        string? observedFingerprint = null;
        await using var session = CreateSession(request, fingerprint => observedFingerprint = fingerprint);
        await session.ConnectAsync(cancellationToken);
        var fingerprint = string.IsNullOrWhiteSpace(request.HostFingerprint) ? observedFingerprint : request.HostFingerprint;
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new InvalidOperationException("服务器没有提供 SSH 主机公钥。");
        var ubuntu = await new UbuntuValidator().ValidateAsync(session, cancellationToken);
        if (!ubuntu.IsSupported) return new(false, ubuntu.FailureReason ?? "仅支持 Ubuntu 服务器。", fingerprint);
        var access = await new RemoteRootContextFactory(new RequestCredentials(request)).CreateWithResolutionAsync(session, cancellationToken);
        var context = access.Context;
        var identity = await context.RootShell.ExecuteAsync("id -u", cancellationToken);
        return identity.Succeeded && identity.StandardOutput.Trim() == "0"
            ? new(true, $"连接成功：Ubuntu {ubuntu.Architecture}，{PrivilegeMethodText(access.Resolution.Method)}，SSH 主机公钥已固定。", fingerprint)
            : new(false, $"提权探测未获得 root。{access.Resolution.FailureReason}", fingerprint);
    }

    public async Task<RealityKeyPair> GenerateRealityKeysAsync(ServerConnectionRequest request, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession(request);
        await session.ConnectAsync(cancellationToken);
        var context = await CreateContextAsync(session, request, cancellationToken);
        var installer = new XrayInstaller(context);
        return await new RemoteRealityKeyGenerator(context, installer).GenerateAsync(cancellationToken);
    }

    public async Task<IssuedCertificate> IssueCertificateAsync(ServerConnectionRequest request, CertificateRequest certificate, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession(request);
        await session.ConnectAsync(cancellationToken);
        return await new CertbotCertificateProvisioner(await CreateContextAsync(session, request, cancellationToken)).IssueAsync(certificate, cancellationToken);
    }

    public async Task<RealityDeploymentResult> DeployAsync(ServerConnectionRequest request, ServerProfile profile, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession(request);
        await session.ConnectAsync(cancellationToken);
        var context = await CreateContextAsync(session, request, cancellationToken);
        return await new RealityRemoteDeploymentService(context, new XrayInstaller(context)).DeployAsync(profile, cancellationToken);
    }

    public async Task<XrayServiceStatus> GetXrayServiceStatusAsync(ServerConnectionRequest request, CancellationToken cancellationToken = default)
    {
        await using var session = CreateSession(request);
        await session.ConnectAsync(cancellationToken);
        var context = await CreateContextAsync(session, request, cancellationToken);
        return await new XrayServiceHealthChecker(context).CheckAsync(cancellationToken);
    }

    private static SshNetRemoteSession CreateSession(ServerConnectionRequest request, Action<string>? hostKeyObserved = null) => new(new SshConnectionSettings(
        request.Host, request.Port, request.UserName, request.HostFingerprint,
        request.SshPasswordProvider, request.PrivateKeyPath, request.PrivateKeyPassphraseProvider, hostKeyObserved));
    private static Task<IRemoteRootContext> CreateContextAsync(SshNetRemoteSession session, ServerConnectionRequest request, CancellationToken cancellationToken) =>
        new RemoteRootContextFactory(new RequestCredentials(request)).CreateAsync(session, cancellationToken);
    private static string PrivilegeMethodText(PrivilegeMethod method) => method switch
    {
        PrivilegeMethod.RootLogin => "当前为 root 登录",
        PrivilegeMethod.PasswordlessSudo => "检测到免密 sudo",
        PrivilegeMethod.PasswordSudo => "sudo 密码验证成功",
        PrivilegeMethod.Su => "root 密码验证成功（su）",
        _ => "提权状态未知"
    };

    private sealed class RequestCredentials(ServerConnectionRequest request) : IPrivilegeCredentialProvider
    {
        // Password-login cloud users normally use the same password for sudo.
        // A dedicated sudo value wins when supplied, otherwise reuse the SSH
        // password only for the sudo probe and privileged command wrapper.
        public ValueTask<string?> GetSudoPasswordAsync(CancellationToken cancellationToken = default) =>
            request.SudoPasswordProvider?.Invoke(cancellationToken) ??
            request.SshPasswordProvider?.Invoke(cancellationToken) ?? new((string?)null);
        public ValueTask<string?> GetRootPasswordAsync(CancellationToken cancellationToken = default) => request.RootPasswordProvider?.Invoke(cancellationToken) ?? new((string?)null);
    }
}
