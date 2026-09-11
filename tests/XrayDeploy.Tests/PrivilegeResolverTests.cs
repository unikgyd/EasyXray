using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class PrivilegeResolverTests
{
    [Fact]
    public async Task SelectsRootLoginWhenSshUserIsRoot()
    {
        var shell = new FakeShell((command) => command == "id -u" ? Ok("0") : Ok());
        var result = await new PrivilegeResolver(new Credentials()).ResolveAsync(shell);

        Assert.Equal(PrivilegeMethod.RootLogin, result.Method);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task SelectsPasswordlessSudoBeforeRequestingSecrets()
    {
        var credentials = new Credentials();
        var shell = new FakeShell(command => command switch
        {
            "id -u" => Ok("1000"),
            "command -v sudo >/dev/null 2>&1" => Ok(),
            "sudo -n true" => Ok(),
            _ => Ok()
        });

        var result = await new PrivilegeResolver(credentials).ResolveAsync(shell);

        Assert.Equal(PrivilegeMethod.PasswordlessSudo, result.Method);
        Assert.Equal(0, credentials.SudoRequests);
        Assert.Equal(0, credentials.RootRequests);
    }

    [Fact]
    public async Task TriesSuAfterSudoPasswordFails()
    {
        var credentials = new Credentials(sudo: "bad", root: "root");
        var shell = new FakeShell(command =>
        {
            if (command == "id -u") return Ok("1000");
            if (command == "command -v sudo >/dev/null 2>&1") return Ok();
            if (command == "sudo -n true") return Fail();
            if (command.Contains("sudo -S", StringComparison.Ordinal)) return Fail();
            if (command.Contains("su -c", StringComparison.Ordinal)) return Ok();
            return Fail();
        });

        var result = await new PrivilegeResolver(credentials).ResolveAsync(shell);

        Assert.Equal(PrivilegeMethod.Su, result.Method);
        Assert.Equal(1, credentials.SudoRequests);
        Assert.Equal(1, credentials.RootRequests);
    }

    [Fact]
    public async Task ExplainsWhenNeitherSudoPasswordNorRootPasswordWasProvided()
    {
        var shell = new FakeShell(command => command switch
        {
            "id -u" => Ok("1000"),
            "command -v sudo >/dev/null 2>&1" => Ok(),
            "sudo -n true" => Fail(),
            _ => Fail()
        });

        var result = await new PrivilegeResolver(new Credentials()).ResolveAsync(shell);

        Assert.False(result.IsAvailable);
        Assert.Contains("无法获得 root 权限", result.FailureReason);
        Assert.Contains(result.Diagnostics!, item => item.Contains("未提供 sudo 密码", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics!, item => item.Contains("未提供 root 密码", StringComparison.Ordinal));
    }

    private static RemoteCommandResult Ok(string output = "") => new(0, output, "");
    private static RemoteCommandResult Fail() => new(1, "", "failed");

    private sealed class FakeShell(Func<string, RemoteCommandResult> execute) : IRemoteShell
    {
        public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult(execute(command));
    }

    private sealed class Credentials(string? sudo = null, string? root = null) : IPrivilegeCredentialProvider
    {
        public int SudoRequests { get; private set; }
        public int RootRequests { get; private set; }
        public ValueTask<string?> GetSudoPasswordAsync(CancellationToken cancellationToken = default) { SudoRequests++; return new(sudo); }
        public ValueTask<string?> GetRootPasswordAsync(CancellationToken cancellationToken = default) { RootRequests++; return new(root); }
    }
}
