using System.Security.Cryptography;

namespace XrayDeploy.Core;

public sealed class RemoteFileStager(IRemoteFileTransfer transfer, IRemoteShell userShell, IPrivilegedShell rootShell) : IRemoteFileStager
{
    private string? _stagingDirectory;

    public async Task<StagedRemoteFile> StageAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A staged file name must not contain a path separator.", nameof(fileName));

        _stagingDirectory ??= "/tmp/xraydeploy-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var createDirectory = await userShell.ExecuteAsync($"mkdir -p {PosixShellEscaper.Quote(_stagingDirectory)} && chmod 0700 {PosixShellEscaper.Quote(_stagingDirectory)}", cancellationToken);
        if (!createDirectory.Succeeded)
            throw new InvalidOperationException($"Could not create remote staging directory: {createDirectory.StandardError}");

        var remotePath = _stagingDirectory + "/" + fileName;
        await transfer.UploadAsync(content, remotePath, cancellationToken);
        return new StagedRemoteFile(remotePath);
    }

    public async Task InstallAsync(StagedRemoteFile stagedFile, string destinationPath, string mode = "0600", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!destinationPath.StartsWith("/", StringComparison.Ordinal) || _stagingDirectory is null || !stagedFile.RemotePath.StartsWith(_stagingDirectory + "/", StringComparison.Ordinal))
            throw new ArgumentException("Only files from this staging directory may be installed to an absolute remote path.");

        var parent = destinationPath[..destinationPath.LastIndexOf('/')];
        var command = $"install -d {PosixShellEscaper.Quote(parent)} && install -m {PosixShellEscaper.Quote(mode)} {PosixShellEscaper.Quote(stagedFile.RemotePath)} {PosixShellEscaper.Quote(destinationPath)}";
        var result = await rootShell.ExecuteAsync(command, cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not install staged file: {result.StandardError}");
    }

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (_stagingDirectory is null) return;
        await rootShell.ExecuteAsync($"rm -rf {PosixShellEscaper.Quote(_stagingDirectory)}", cancellationToken);
        _stagingDirectory = null;
    }
}
