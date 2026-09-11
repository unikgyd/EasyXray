using System.Text.Json;

namespace XrayDeploy.Core;

public sealed class ServerProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task<ServerProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ServerProfile>(source, Options, cancellationToken)
            ?? throw new InvalidDataException("The server profile is empty or invalid.");
    }

    public async Task SaveAsync(ServerProfile profile, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(parent!);
        var temporary = path + ".new-" + Guid.NewGuid().ToString("N");
        await using (var target = File.Create(temporary)) await JsonSerializer.SerializeAsync(target, profile, Options, cancellationToken);
        File.Move(temporary, path, true);
    }
}
