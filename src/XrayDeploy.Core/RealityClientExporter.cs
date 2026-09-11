namespace XrayDeploy.Core;

public sealed record RealityClientExport(string VlessUri, string StructuredProfile);

public sealed class RealityClientExporter
{
    public RealityClientExport Export(RealityInboundProfile inbound, RealityUser user, string serverAddress, string? serverDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);
        if (!inbound.Users.Any(candidate => candidate.Id == user.Id))
            throw new ArgumentException("The selected user does not belong to this REALITY inbound.", nameof(user));

        var serverName = inbound.ServerNames.FirstOrDefault() ?? throw new InvalidOperationException("The inbound has no server name.");
        var shortId = inbound.ShortIds.FirstOrDefault() ?? throw new InvalidOperationException("The inbound has no short id.");
        var query = new Dictionary<string, string>
        {
            ["encryption"] = "none",
            ["flow"] = user.Flow,
            ["security"] = "reality",
            ["sni"] = serverName,
            ["fp"] = "chrome",
            ["pbk"] = inbound.PublicKey,
            ["sid"] = shortId,
            ["type"] = "tcp"
        };
        var parameters = string.Join("&", query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        var label = Uri.EscapeDataString(serverDisplayName ?? $"{inbound.Name}-{user.Name}");
        var uri = $"vless://{user.Id:D}@{serverAddress}:{inbound.ListenPort}?{parameters}#{label}";
        var structured = System.Text.Json.JsonSerializer.Serialize(new
        {
            protocol = "vless-reality",
            name = serverDisplayName ?? inbound.Name,
            address = serverAddress,
            port = inbound.ListenPort,
            userId = user.Id,
            flow = user.Flow,
            serverName,
            publicKey = inbound.PublicKey,
            shortId,
            fingerprint = "chrome"
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return new(uri, structured);
    }
}
