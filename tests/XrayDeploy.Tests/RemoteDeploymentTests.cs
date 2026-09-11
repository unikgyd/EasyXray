using XrayDeploy.Core;
using XrayDeploy.Infrastructure;

namespace XrayDeploy.Tests;

public sealed class RemoteDeploymentTests
{
    [Fact]
    public async Task RestoresThePreviousConfigWhenRestartFails()
    {
        var root = new RecordingShell(restartFailures: 1);
        var user = new RecordingShell();
        var transfer = new RecordingTransfer();
        var context = new RemoteRootContext(user, root, new RemoteFileStager(transfer, user, root));
        var service = new RealityRemoteDeploymentService(context, new XrayInstaller(context));

        var result = await service.DeployAsync(RealityConfigCompilerTests.CreateValidServer());

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Single(transfer.Uploads);
        Assert.Contains(root.Commands, command => command.Contains("run -test", StringComparison.Ordinal));
        Assert.Equal(2, root.Commands.Count(command => command.Contains("systemctl restart xray.service", StringComparison.Ordinal)));
        Assert.Contains(root.Commands, command => command.Contains("rm -rf '/tmp/xraydeploy-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotReplaceConfigWhenRemoteValidationFails()
    {
        var root = new RecordingShell(validationFails: true);
        var user = new RecordingShell();
        var transfer = new RecordingTransfer();
        var context = new RemoteRootContext(user, root, new RemoteFileStager(transfer, user, root));
        var service = new RealityRemoteDeploymentService(context, new XrayInstaller(context));

        var result = await service.DeployAsync(RealityConfigCompilerTests.CreateValidServer());

        Assert.False(result.Succeeded);
        Assert.False(result.RolledBack);
        Assert.DoesNotContain(root.Commands, command => command.Contains("systemctl restart", StringComparison.Ordinal));
        Assert.Single(transfer.Uploads);
    }

    [Fact]
    public async Task InstallsXrayWhenItIsMissingBeforeValidatingTheConfiguration()
    {
        var root = new MissingXrayShell();
        var user = new RecordingShell();
        var transfer = new RecordingTransfer();
        var context = new RemoteRootContext(user, root, new RemoteFileStager(transfer, user, root));
        var service = new RealityRemoteDeploymentService(context, new XrayInstaller(context));

        var result = await service.DeployAsync(RealityConfigCompilerTests.CreateValidServer());

        Assert.True(result.Succeeded);
        var installIndex = root.Commands.FindIndex(command => command.Contains("xray-install.sh install", StringComparison.Ordinal));
        var validationIndex = root.Commands.FindIndex(command => command.Contains("run -test", StringComparison.Ordinal));
        Assert.True(installIndex >= 0, "The Xray install command was not issued.");
        Assert.True(validationIndex > installIndex, "Configuration validation must happen after Xray installation.");
        Assert.Contains(root.Commands, command => command.Contains("install -m 0640", StringComparison.Ordinal));
        Assert.Contains(root.Commands, command => command.Contains("chown root:", StringComparison.Ordinal));
    }

    private sealed class RecordingShell(bool validationFails = false, int restartFailures = 0) : IRemoteShell, IPrivilegedShell
    {
        private int _restartFailures = restartFailures;
        public List<string> Commands { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (command.Contains("run -test", StringComparison.Ordinal)) return Task.FromResult(validationFails ? Fail("invalid config") : Ok());
            if (command.Contains("systemctl restart xray.service", StringComparison.Ordinal) && _restartFailures-- > 0) return Task.FromResult(Fail("restart failed"));
            return Task.FromResult(Ok());
        }
    }

    private sealed class RecordingTransfer : IRemoteFileTransfer
    {
        public List<string> Uploads { get; } = [];
        public async Task UploadAsync(Stream content, string remotePath, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(content, leaveOpen: true);
            Uploads.Add(await reader.ReadToEndAsync(cancellationToken));
        }
    }

    private sealed class MissingXrayShell : IRemoteShell, IPrivilegedShell
    {
        private bool _xrayInstalled;
        public List<string> Commands { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (command.StartsWith("test -x ", StringComparison.Ordinal) && command.Contains("/usr/local/bin/xray", StringComparison.Ordinal))
                return Task.FromResult(_xrayInstalled ? Ok() : Fail("not installed"));
            if (command.Contains("xray-install.sh install", StringComparison.Ordinal))
            {
                _xrayInstalled = true;
                return Task.FromResult(Ok());
            }
            return Task.FromResult(Ok());
        }
    }

    private static RemoteCommandResult Ok() => new(0, "", "");
    private static RemoteCommandResult Fail(string error) => new(1, "", error);
}
