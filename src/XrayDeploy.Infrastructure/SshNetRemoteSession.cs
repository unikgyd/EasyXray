using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Common;
using XrayDeploy.Core;

namespace XrayDeploy.Infrastructure;

public sealed record SshConnectionSettings(
    string Host,
    int Port,
    string UserName,
    string? ExpectedHostKeySha256,
    Func<CancellationToken, ValueTask<string?>>? PasswordProvider = null,
    string? PrivateKeyPath = null,
    Func<CancellationToken, ValueTask<string?>>? PrivateKeyPassphraseProvider = null,
    Action<string>? HostKeyObserved = null);

public sealed class SshHostKeyVerifier
{
    private readonly byte[] _expected;

    public SshHostKeyVerifier(string expectedSha256) => _expected = Parse(expectedSha256);

    public bool IsMatch(byte[] hostKey) => CryptographicOperations.FixedTimeEquals(
        _expected, SHA256.HashData(hostKey));

    public static string Format(byte[] hostKey) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    public static byte[] Parse(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var value = fingerprint.Trim();
        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
        return Convert.FromBase64String(value);
    }
}

public sealed class SshNetRemoteSession : IRemoteShell, IRemoteFileTransfer, IAsyncDisposable
{
    private readonly SshConnectionSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SshClient? _ssh;
    private SftpClient? _sftp;

    public SshNetRemoteSession(SshConnectionSettings settings) => _settings = settings;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ssh?.IsConnected == true) return;
            var connectionInfo = await CreateConnectionInfoAsync(cancellationToken);
            _ssh = new SshClient(connectionInfo);
            ConfigureHostKeyVerification(_ssh);
            await Task.Run(_ssh.Connect, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<RemoteCommandResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() => _ssh!.RunCommand(command), cancellationToken);
            return new(result.ExitStatus ?? -1, result.Result, result.Error);
        }
        finally { _gate.Release(); }
    }

    public async Task UploadAsync(Stream content, string remotePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_sftp?.IsConnected != true)
            {
                var connectionInfo = await CreateConnectionInfoAsync(cancellationToken);
                _sftp?.Dispose();
                _sftp = new SftpClient(connectionInfo);
                ConfigureHostKeyVerification(_sftp);
                await Task.Run(_sftp.Connect, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => _sftp!.UploadFile(content, remotePath, true), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<ConnectionInfo> CreateConnectionInfoAsync(CancellationToken cancellationToken)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(_settings.PrivateKeyPath))
        {
            PrivateKeyFile key;
            try
            {
                key = new PrivateKeyFile(_settings.PrivateKeyPath);
            }
            catch (SshPassPhraseNullOrEmptyException) when (_settings.PrivateKeyPassphraseProvider is not null)
            {
                var passphrase = await _settings.PrivateKeyPassphraseProvider(cancellationToken);
                if (string.IsNullOrEmpty(passphrase)) throw new InvalidOperationException("The SSH private key requires a passphrase.");
                key = new PrivateKeyFile(_settings.PrivateKeyPath, passphrase);
            }
            methods.Add(new PrivateKeyAuthenticationMethod(_settings.UserName, key));
        }
        if (_settings.PasswordProvider is not null)
        {
            var password = await _settings.PasswordProvider(cancellationToken) ?? throw new InvalidOperationException("An SSH password is required.");
            methods.Add(new PasswordAuthenticationMethod(_settings.UserName, password));
        }
        if (methods.Count == 0)
        {
            throw new InvalidOperationException("Provide an SSH password or private key.");
        }

        var info = new ConnectionInfo(_settings.Host, _settings.Port, _settings.UserName, methods.ToArray());
        return info;
    }

    private void ConfigureHostKeyVerification(BaseClient client)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ExpectedHostKeySha256))
        {
            var verifier = new SshHostKeyVerifier(_settings.ExpectedHostKeySha256);
            client.HostKeyReceived += (_, args) => args.CanTrust = verifier.IsMatch(args.HostKey);
            return;
        }

        // Trust-on-first-use is limited to a connection explicitly lacking a pin.
        // The caller receives the observed value and persists it for strict checks.
        client.HostKeyReceived += (_, args) =>
        {
            _settings.HostKeyObserved?.Invoke(SshHostKeyVerifier.Format(args.HostKey));
            args.CanTrust = true;
        };
    }

    public ValueTask DisposeAsync()
    {
        _sftp?.Dispose();
        _ssh?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class RemoteRootContextFactory(IPrivilegeCredentialProvider credentials)
{
    public async Task<IRemoteRootContext> CreateAsync(SshNetRemoteSession session, CancellationToken cancellationToken = default)
    {
        var access = await CreateWithResolutionAsync(session, cancellationToken);
        return access.Context;
    }

    public async Task<ResolvedRemoteRootContext> CreateWithResolutionAsync(SshNetRemoteSession session, CancellationToken cancellationToken = default)
    {
        var resolution = await new PrivilegeResolver(credentials).ResolveAsync(session, cancellationToken);
        if (!resolution.IsAvailable)
            throw new InvalidOperationException(string.Join(" ", new[] { resolution.FailureReason ?? "Privileged remote access is unavailable." }
                .Concat(resolution.Diagnostics ?? [])));
        var context = new RemoteRootContext(session, resolution.RootShell!, new RemoteFileStager(session, session, resolution.RootShell!));
        return new(context, resolution);
    }
}

public sealed record ResolvedRemoteRootContext(IRemoteRootContext Context, PrivilegeResolution Resolution);
