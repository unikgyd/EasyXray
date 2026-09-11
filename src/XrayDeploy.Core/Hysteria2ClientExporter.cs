namespace XrayDeploy.Core;

public sealed record Hysteria2ClientExport(string Hy2Uri, string StructuredProfile);

public sealed class Hysteria2ClientExporter
{
    public Hysteria2ClientExport Export(Hysteria2InboundProfile inbound, Hysteria2User user, string serverAddress, string? displayName = null)
    {
        if (!inbound.Users.Contains(user)) throw new ArgumentException("The selected user does not belong to this Hysteria2 inbound.", nameof(user));
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);
        if (string.IsNullOrWhiteSpace(inbound.CertificateDomain)) throw new InvalidOperationException("A certificate domain is required for a secure Hysteria2 export.");
        var query = new Dictionary<string, string> { ["sni"] = inbound.CertificateDomain };
        if (!string.IsNullOrWhiteSpace(inbound.Obfuscation)) { query["obfs"] = "salamander"; query["obfs-password"] = inbound.Obfuscation; }
        var parameters = string.Join("&", query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        var name = displayName ?? $"{inbound.Name}-{user.Name}";
        var uri = $"hysteria2://{Uri.EscapeDataString(user.Authentication)}@{serverAddress}:{inbound.ListenPort}/?{parameters}#{Uri.EscapeDataString(name)}";
        var structured = System.Text.Json.JsonSerializer.Serialize(new { protocol = "hysteria2", name, address = serverAddress, port = inbound.ListenPort, authentication = user.Authentication, sni = inbound.CertificateDomain, obfuscation = inbound.Obfuscation }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return new(uri, structured);
    }
}
