using XrayDeploy.Core;
using XrayDeploy.Infrastructure;

namespace XrayDeploy.Tests;

public sealed class RealityKeyGeneratorTests
{
    [Fact]
    public async Task ParsesCurrentXrayX25519Output()
    {
        var root = new FakeShell("""
            PrivateKey: private-key
            Password (PublicKey): public-key
            Hash32: hash
            """);
        var context = new RemoteRootContext(root, root, new FakeStager());
        var pair = await new RemoteRealityKeyGenerator(context, new XrayInstaller(context)).GenerateAsync();

        Assert.Equal("private-key", pair.PrivateKey);
        Assert.Equal("public-key", pair.PublicKey);
    }

    private sealed class FakeShell(string keys) : IRemoteShell, IPrivilegedShell
    {
        public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Contains("x25519", StringComparison.Ordinal) ? new RemoteCommandResult(0, keys, "") : new RemoteCommandResult(0, "", ""));
    }

    private sealed class FakeStager : IRemoteFileStager
    {
        public Task<StagedRemoteFile> StageAsync(Stream content, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InstallAsync(StagedRemoteFile stagedFile, string destinationPath, string mode = "0600", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CleanupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
