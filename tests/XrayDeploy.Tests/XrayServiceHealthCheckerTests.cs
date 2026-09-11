using XrayDeploy.Core;
using XrayDeploy.Infrastructure;

namespace XrayDeploy.Tests;

public sealed class XrayServiceHealthCheckerTests
{
    [Fact]
    public async Task ReportsAnActiveInstalledService()
    {
        var status = await new XrayServiceHealthChecker(CreateContext(new StatusShell(active: true))).CheckAsync();

        Assert.True(status.IsInstalled);
        Assert.True(status.IsActive);
        Assert.Contains("运行正常", status.Message);
    }

    [Fact]
    public async Task IncludesTheLastRelevantJournalErrorForAFailedService()
    {
        var status = await new XrayServiceHealthChecker(CreateContext(new StatusShell(active: false))).CheckAsync();

        Assert.True(status.IsInstalled);
        Assert.False(status.IsActive);
        Assert.Contains("permission denied", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IRemoteRootContext CreateContext(IPrivilegedShell root) => new RemoteRootContext(root, root, new NoopStager());

    private sealed class StatusShell(bool active) : IPrivilegedShell
    {
        public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            if (command.Contains("LoadState", StringComparison.Ordinal)) return Task.FromResult(new RemoteCommandResult(0, "loaded\n", ""));
            if (command.Contains("is-active", StringComparison.Ordinal)) return Task.FromResult(active ? new RemoteCommandResult(0, "", "") : new RemoteCommandResult(3, "", "inactive"));
            if (command.Contains("journalctl", StringComparison.Ordinal)) return Task.FromResult(new RemoteCommandResult(0, "Failed to start: open config.json: permission denied\n", ""));
            return Task.FromResult(new RemoteCommandResult(0, "", ""));
        }
    }

    private sealed class NoopStager : IRemoteFileStager
    {
        public Task<StagedRemoteFile> StageAsync(Stream content, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InstallAsync(StagedRemoteFile stagedFile, string destinationPath, string mode = "0600", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CleanupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
