using System.Text.Json;
using System.IO;

namespace EasyXray.Services;

/// <summary>
/// A small TOFU registry. The first successful connection pins the server's SSH
/// host key; every later connection to the same host and port is strict.
/// </summary>
public sealed class KnownHostStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KnownHostStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EasyXray", "known-hosts.json");

    public async Task<string?> FindAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            return entries.GetValueOrDefault(Key(host, port));
        }
        finally { _gate.Release(); }
    }

    public async Task PinAsync(string host, int port, string fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            entries[Key(host, port)] = fingerprint;
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".new-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
        await using var source = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(source, cancellationToken: cancellationToken)
            ?? new(StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";
}
