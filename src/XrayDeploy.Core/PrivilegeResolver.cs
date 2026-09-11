namespace XrayDeploy.Core;

public enum PrivilegeMethod
{
    RootLogin,
    PasswordlessSudo,
    PasswordSudo,
    Su,
    Unavailable
}

public sealed record PrivilegeResolution(PrivilegeMethod Method, IPrivilegedShell? RootShell, string? FailureReason = null, IReadOnlyList<string>? Diagnostics = null)
{
    public bool IsAvailable => RootShell is not null;
}

public interface IPrivilegeCredentialProvider
{
    ValueTask<string?> GetSudoPasswordAsync(CancellationToken cancellationToken = default);
    ValueTask<string?> GetRootPasswordAsync(CancellationToken cancellationToken = default);
}

public sealed class PrivilegeResolver(IPrivilegeCredentialProvider credentials)
{
    public async Task<PrivilegeResolution> ResolveAsync(IRemoteShell userShell, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var identity = await userShell.ExecuteAsync("id -u", cancellationToken);
        if (!identity.Succeeded)
            return new(PrivilegeMethod.Unavailable, null, "无法确定远端登录用户的身份。", diagnostics);

        if (identity.StandardOutput.Trim() == "0")
            return new(PrivilegeMethod.RootLogin, new RootShell(userShell), Diagnostics: ["SSH 登录用户已经是 root。"]) ;

        var sudoExists = await userShell.ExecuteAsync("command -v sudo >/dev/null 2>&1", cancellationToken);
        if (sudoExists.Succeeded)
        {
            var passwordless = await userShell.ExecuteAsync("sudo -n true", cancellationToken);
            if (passwordless.Succeeded)
                return new(PrivilegeMethod.PasswordlessSudo, new PasswordlessSudoShell(userShell), Diagnostics: ["检测到免密 sudo。"]);

            diagnostics.Add("sudo 存在，但当前登录用户不能免密提权。");

            var sudoPassword = await credentials.GetSudoPasswordAsync(cancellationToken);
            if (!string.IsNullOrEmpty(sudoPassword))
            {
                var candidate = new PasswordSudoShell(userShell, sudoPassword);
                var probe = await candidate.ExecuteAsync("true", cancellationToken);
                if (probe.Succeeded)
                    return new(PrivilegeMethod.PasswordSudo, candidate, Diagnostics: [.. diagnostics, "sudo 密码验证成功。"]);
                diagnostics.Add("提供的 sudo 密码被拒绝，或该用户不在 sudoers 中。");
            }
            else diagnostics.Add("未提供 sudo 密码，已跳过带密码 sudo。 ");
        }
        else diagnostics.Add("服务器未安装 sudo，或当前环境禁止使用 sudo。 ");

        var rootPassword = await credentials.GetRootPasswordAsync(cancellationToken);
        if (!string.IsNullOrEmpty(rootPassword))
        {
            var candidate = new SuShell(userShell, rootPassword);
            var probe = await candidate.ExecuteAsync("true", cancellationToken);
            if (probe.Succeeded)
                return new(PrivilegeMethod.Su, candidate, Diagnostics: [.. diagnostics, "root 密码验证成功，使用 su 提权。"]);
            diagnostics.Add("提供的 root 密码被拒绝，或服务器禁用了 su。 ");
        }
        else diagnostics.Add("未提供 root 密码，已跳过 su。 ");

        return new(PrivilegeMethod.Unavailable, null, "无法获得 root 权限。请使用 root 登录，或填写正确的 sudo / root 密码，并确认该用户获授权。", diagnostics);
    }
}

public sealed class RootShell(IRemoteShell userShell) : IPrivilegedShell
{
    public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
        userShell.ExecuteAsync(PosixShellEscaper.Command(command), cancellationToken);
}

public sealed class PasswordlessSudoShell(IRemoteShell userShell) : IPrivilegedShell
{
    public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
        userShell.ExecuteAsync($"sudo -n {PosixShellEscaper.Command(command)}", cancellationToken);
}

public sealed class PasswordSudoShell(IRemoteShell userShell, string password) : IPrivilegedShell
{
    public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
        userShell.ExecuteAsync($"printf '%s\\n' {PosixShellEscaper.Quote(password)} | sudo -S -p '' {PosixShellEscaper.Command(command)}", cancellationToken);
}

public sealed class SuShell(IRemoteShell userShell, string password) : IPrivilegedShell
{
    public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
        userShell.ExecuteAsync($"printf '%s\\n' {PosixShellEscaper.Quote(password)} | su -c {PosixShellEscaper.Quote($"sh -lc {PosixShellEscaper.Quote(command)}")}", cancellationToken);
}
