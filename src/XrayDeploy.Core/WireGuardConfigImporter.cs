namespace XrayDeploy.Core;

public sealed class WireGuardConfigImporter
{
    public WireGuardOutboundProfile Import(string config, string name, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config);
        var sections = ParseSections(config);
        if (!sections.TryGetValue("Interface", out var @interface) || !sections.TryGetValue("Peer", out var peer))
            throw new FormatException("A WireGuard configuration must contain [Interface] and [Peer] sections.");
        var endpoint = Required(peer, "Endpoint");
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(endpoint[(separator + 1)..], out var endpointPort)) throw new FormatException("WireGuard Endpoint must be host:port.");

        var profile = new WireGuardOutboundProfile
        {
            Name = name,
            Tag = tag,
            PrivateKey = Required(@interface, "PrivateKey"),
            EndpointHost = endpoint[..separator].Trim('[', ']'),
            EndpointPort = endpointPort,
            PeerPublicKey = Required(peer, "PublicKey"),
            PresharedKey = peer.GetValueOrDefault("PresharedKey"),
            Mtu = OptionalInt(@interface, "MTU"),
            PersistentKeepalive = OptionalInt(peer, "PersistentKeepalive")
        };
        foreach (var address in Required(@interface, "Address").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            profile.LocalAddresses.Add(address);
        foreach (var address in Required(peer, "AllowedIPs").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            profile.AllowedIPs.Add(address);
        return profile;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSections(string config)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { current = new(StringComparer.OrdinalIgnoreCase); result[line[1..^1]] = current; continue; }
            var separator = line.IndexOf('=');
            if (current is null || separator <= 0) throw new FormatException("Invalid WireGuard configuration line.");
            current[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) => values.GetValueOrDefault(name) is { Length: > 0 } value ? value : throw new FormatException($"Missing WireGuard value: {name}.");
    private static int? OptionalInt(IReadOnlyDictionary<string, string> values, string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? int.Parse(value) : null;
}
