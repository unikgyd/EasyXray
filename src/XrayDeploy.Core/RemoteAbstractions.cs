namespace XrayDeploy.Core;

public sealed record RemoteCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IRemoteShell
{
    Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default);
}

public interface IPrivilegedShell : IRemoteShell { }

public interface IRemoteFileTransfer
{
    Task UploadAsync(Stream content, string remotePath, CancellationToken cancellationToken = default);
}

public interface IRemoteFileStager
{
    Task<StagedRemoteFile> StageAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
    Task InstallAsync(StagedRemoteFile stagedFile, string destinationPath, string mode = "0600", CancellationToken cancellationToken = default);
    Task CleanupAsync(CancellationToken cancellationToken = default);
}

public interface IRemoteRootContext
{
    IRemoteShell UserShell { get; }
    IPrivilegedShell RootShell { get; }
    IRemoteFileStager FileStager { get; }
}

public sealed record StagedRemoteFile(string RemotePath);

public sealed class RemoteRootContext(IRemoteShell userShell, IPrivilegedShell rootShell, IRemoteFileStager fileStager) : IRemoteRootContext
{
    public IRemoteShell UserShell { get; } = userShell;
    public IPrivilegedShell RootShell { get; } = rootShell;
    public IRemoteFileStager FileStager { get; } = fileStager;
}
