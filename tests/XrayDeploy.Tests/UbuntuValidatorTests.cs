using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class UbuntuValidatorTests
{
    [Fact]
    public async Task AcceptsUbuntuOnSupportedArchitecture()
    {
        var shell = new FakeShell(command => command switch
        {
            "cat /etc/os-release" => new(0, "NAME=Ubuntu\nID=ubuntu\n", ""),
            "uname -m" => new(0, "aarch64\n", ""),
            _ => new(1, "", "unknown")
        });

        var result = await new UbuntuValidator().ValidateAsync(shell);

        Assert.True(result.IsSupported);
        Assert.Equal("aarch64", result.Architecture);
    }

    [Fact]
    public async Task RejectsNonUbuntuBeforeCheckingArchitecture()
    {
        var shell = new FakeShell(command => new(0, "ID=debian\n", ""));
        var result = await new UbuntuValidator().ValidateAsync(shell);
        Assert.False(result.IsSupported);
        Assert.Contains("Ubuntu", result.FailureReason);
    }

    private sealed class FakeShell(Func<string, RemoteCommandResult> execute) : IRemoteShell
    {
        public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult(execute(command));
    }
}
