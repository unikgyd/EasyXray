using System.Net;

namespace XrayDeploy.Core;

public sealed record ValidationIssue(string Path, string Message);

public sealed class ValidationResult
{
    private readonly List<ValidationIssue> _issues = [];
    public IReadOnlyList<ValidationIssue> Issues => _issues;
    public bool IsValid => _issues.Count == 0;
    public void Add(string path, string message) => _issues.Add(new(path, message));
    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new InvalidOperationException("Profile validation failed: " + string.Join("; ", _issues.Select(issue => $"{issue.Path}: {issue.Message}")));
    }
}

public sealed class RealityTopologyValidator
{
    public ValidationResult Validate(ServerProfile server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(server.Name)) result.Add("Server.Name", "is required.");

        var enabledInbounds = server.Inbounds.Where(inbound => inbound.Enabled).ToList();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inbound in server.Inbounds)
        {
            ValidateBase(inbound, $"Inbound[{inbound.Name}]", tags, result);
            switch (inbound)
            {
                case RealityInboundProfile reality: ValidateRealityInbound(reality, result); break;
                case Hysteria2InboundProfile hysteria: ValidateHysteriaInbound(hysteria, result); break;
                default: result.Add($"Inbound[{inbound.Name}]", "only REALITY and Hysteria2 inbounds are supported."); break;
            }
        }

        foreach (var outbound in server.Outbounds)
        {
            ValidateBase(outbound, $"Outbound[{outbound.Name}]", tags, result);
            switch (outbound)
            {
                case RealityOutboundProfile reality: ValidateRealityOutbound(reality, result); break;
                case Hysteria2OutboundProfile hysteria: ValidateHysteriaOutbound(hysteria, result); break;
                case WireGuardOutboundProfile wireGuard: ValidateWireGuardOutbound(wireGuard, result); break;
                default: result.Add($"Outbound[{outbound.Name}]", "only REALITY, Hysteria2 and WireGuard outbounds are supported."); break;
            }
        }

        foreach (var group in enabledInbounds.GroupBy(inbound => (inbound.ListenPort, Address: GetListenAddress(inbound), Transport: GetTransport(inbound))))
        {
            if (group.Count() > 1) result.Add("Inbounds", $"listener {group.Key.Address}:{group.Key.ListenPort} is configured more than once.");
        }

        ValidateBindings(server, enabledInbounds, result);
        ValidateBlockRules(server, result);
        return result;
    }

    private static void ValidateBase(InboundProfile profile, string path, ISet<string> tags, ValidationResult result)
    {
        if (profile.Id == Guid.Empty) result.Add(path, "has an empty id.");
        if (string.IsNullOrWhiteSpace(profile.Name)) result.Add(path + ".Name", "is required.");
        if (string.IsNullOrWhiteSpace(profile.Tag)) result.Add(path + ".Tag", "is required.");
        else if (!tags.Add(profile.Tag)) result.Add(path + ".Tag", "must be unique across inbounds and outbounds.");
        if (profile.ListenPort is < 1 or > 65535) result.Add(path + ".ListenPort", "must be between 1 and 65535.");
    }

    private static void ValidateBase(OutboundProfile profile, string path, ISet<string> tags, ValidationResult result)
    {
        if (profile.Id == Guid.Empty) result.Add(path, "has an empty id.");
        if (string.IsNullOrWhiteSpace(profile.Name)) result.Add(path + ".Name", "is required.");
        if (string.IsNullOrWhiteSpace(profile.Tag)) result.Add(path + ".Tag", "is required.");
        else if (!tags.Add(profile.Tag)) result.Add(path + ".Tag", "must be unique across inbounds and outbounds.");
    }

    private static void ValidateRealityInbound(RealityInboundProfile profile, ValidationResult result)
    {
        var path = $"RealityInbound[{profile.Name}]";
        if (string.IsNullOrWhiteSpace(profile.ListenAddress) || IPAddress.TryParse(profile.ListenAddress, out _) is false && profile.ListenAddress != "localhost") result.Add(path + ".ListenAddress", "must be an IP address or localhost.");
        if (profile.Users.Count == 0) result.Add(path + ".Users", "must include at least one user.");
        foreach (var user in profile.Users)
        {
            if (user.Id == Guid.Empty) result.Add(path + ".Users", "contains an empty UUID.");
            if (string.IsNullOrWhiteSpace(user.Name)) result.Add(path + ".Users", "contains a user without a name.");
            if (string.IsNullOrWhiteSpace(user.Flow)) result.Add(path + ".Users", "contains a user without a flow.");
        }
        if (!HostAndPort(profile.Target)) result.Add(path + ".Target", "must be host:port.");
        if (profile.ServerNames.Count == 0 || profile.ServerNames.Any(string.IsNullOrWhiteSpace)) result.Add(path + ".ServerNames", "must contain at least one server name.");
        if (string.IsNullOrWhiteSpace(profile.PrivateKey)) result.Add(path + ".PrivateKey", "is required.");
        if (string.IsNullOrWhiteSpace(profile.PublicKey)) result.Add(path + ".PublicKey", "is required.");
        if (profile.ShortIds.Count == 0 || profile.ShortIds.Any(shortId => !IsHex(shortId) || shortId.Length > 16)) result.Add(path + ".ShortIds", "must contain one or more hexadecimal values of at most 16 characters.");
    }

    private static void ValidateRealityOutbound(RealityOutboundProfile profile, ValidationResult result)
    {
        var path = $"RealityOutbound[{profile.Name}]";
        if (string.IsNullOrWhiteSpace(profile.ServerAddress)) result.Add(path + ".ServerAddress", "is required.");
        if (profile.ServerPort is < 1 or > 65535) result.Add(path + ".ServerPort", "must be between 1 and 65535.");
        if (profile.UserId == Guid.Empty) result.Add(path + ".UserId", "must be a UUID.");
        if (string.IsNullOrWhiteSpace(profile.Flow)) result.Add(path + ".Flow", "is required.");
        if (string.IsNullOrWhiteSpace(profile.ServerName)) result.Add(path + ".ServerName", "is required.");
        if (string.IsNullOrWhiteSpace(profile.PublicKey)) result.Add(path + ".PublicKey", "is required.");
        if (!IsHex(profile.ShortId) || profile.ShortId.Length > 16) result.Add(path + ".ShortId", "must be hexadecimal with at most 16 characters.");
        if (string.IsNullOrWhiteSpace(profile.Fingerprint)) result.Add(path + ".Fingerprint", "is required.");
    }

    private static void ValidateHysteriaInbound(Hysteria2InboundProfile profile, ValidationResult result)
    {
        var path = $"Hysteria2Inbound[{profile.Name}]";
        if (string.IsNullOrWhiteSpace(profile.ListenAddress) || IPAddress.TryParse(profile.ListenAddress, out _) is false && profile.ListenAddress != "localhost") result.Add(path + ".ListenAddress", "must be an IP address or localhost.");
        if (profile.Users.Count == 0 || profile.Users.Any(user => string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Authentication))) result.Add(path + ".Users", "must contain named users with authentication values.");
        if (!IsDnsDomain(profile.CertificateDomain)) result.Add(path + ".CertificateDomain", "must be a valid DNS domain; IP-only Hysteria2 is intentionally unsupported.");
        var pathsProvided = !string.IsNullOrWhiteSpace(profile.TlsCertificatePath) || !string.IsNullOrWhiteSpace(profile.TlsPrivateKeyPath);
        var pemProvided = !string.IsNullOrWhiteSpace(profile.TlsCertificatePem) || !string.IsNullOrWhiteSpace(profile.TlsPrivateKeyPem);
        var localProvided = !string.IsNullOrWhiteSpace(profile.TlsCertificateLocalPath) || !string.IsNullOrWhiteSpace(profile.TlsPrivateKeyLocalPath);
        if (pathsProvided && (string.IsNullOrWhiteSpace(profile.TlsCertificatePath) || string.IsNullOrWhiteSpace(profile.TlsPrivateKeyPath) || !profile.TlsCertificatePath.StartsWith("/", StringComparison.Ordinal) || !profile.TlsPrivateKeyPath.StartsWith("/", StringComparison.Ordinal))) result.Add(path + ".TLS", "certificate and private-key paths must both be absolute remote paths.");
        if (pemProvided && (string.IsNullOrWhiteSpace(profile.TlsCertificatePem) || string.IsNullOrWhiteSpace(profile.TlsPrivateKeyPem))) result.Add(path + ".TLS", "certificate and private-key PEM content must be supplied together.");
        if (localProvided && (string.IsNullOrWhiteSpace(profile.TlsCertificateLocalPath) || string.IsNullOrWhiteSpace(profile.TlsPrivateKeyLocalPath))) result.Add(path + ".TLS", "certificate and private-key local paths must be supplied together.");
        if (!pathsProvided && !pemProvided && !localProvided) result.Add(path + ".TLS", "requires remote TLS paths, PEM content, or local certificate and private-key paths.");
        if (profile.UdpIdleTimeoutSeconds is < 1 or > 3600) result.Add(path + ".UdpIdleTimeoutSeconds", "must be between 1 and 3600.");
    }

    private static void ValidateHysteriaOutbound(Hysteria2OutboundProfile profile, ValidationResult result)
    {
        var path = $"Hysteria2Outbound[{profile.Name}]";
        if (string.IsNullOrWhiteSpace(profile.ServerAddress)) result.Add(path + ".ServerAddress", "is required.");
        if (profile.ServerPort is < 1 or > 65535) result.Add(path + ".ServerPort", "must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(profile.Authentication)) result.Add(path + ".Authentication", "is required.");
        if (string.IsNullOrWhiteSpace(profile.TlsServerName)) result.Add(path + ".TlsServerName", "is required.");
        if (profile.UdpIdleTimeoutSeconds is < 1 or > 3600) result.Add(path + ".UdpIdleTimeoutSeconds", "must be between 1 and 3600.");
    }

    private static void ValidateWireGuardOutbound(WireGuardOutboundProfile profile, ValidationResult result)
    {
        var path = $"WireGuardOutbound[{profile.Name}]";
        if (string.IsNullOrWhiteSpace(profile.EndpointHost)) result.Add(path + ".EndpointHost", "is required.");
        if (profile.EndpointPort is < 1 or > 65535) result.Add(path + ".EndpointPort", "must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(profile.PrivateKey)) result.Add(path + ".PrivateKey", "is required.");
        if (string.IsNullOrWhiteSpace(profile.PeerPublicKey)) result.Add(path + ".PeerPublicKey", "is required.");
        if (profile.LocalAddresses.Count == 0 || profile.LocalAddresses.Any(address => !IsCidr(address))) result.Add(path + ".LocalAddresses", "must contain one or more CIDR addresses.");
        if (profile.AllowedIPs.Count == 0 || profile.AllowedIPs.Any(address => !IsCidr(address))) result.Add(path + ".AllowedIPs", "must contain one or more CIDR addresses.");
        if (profile.Mtu is not null && profile.Mtu is < 576 or > 65535) result.Add(path + ".Mtu", "must be between 576 and 65535.");
        if (profile.PersistentKeepalive is not null && profile.PersistentKeepalive is < 0 or > 65535) result.Add(path + ".PersistentKeepalive", "must be between 0 and 65535.");
    }

    private static void ValidateBindings(ServerProfile server, IReadOnlyCollection<InboundProfile> enabledInbounds, ValidationResult result)
    {
        foreach (var inbound in enabledInbounds)
        {
            var bindings = server.Bindings.Where(binding => binding.Enabled && binding.InboundId == inbound.Id).ToList();
            if (bindings.Count != 1)
            {
                result.Add($"Inbound[{inbound.Name}].Binding", "must have exactly one enabled primary exit.");
                continue;
            }

            if (bindings[0].Destination is OutboundDestination destination)
            {
                var outbound = server.Outbounds.SingleOrDefault(profile => profile.Id == destination.OutboundId);
                if (outbound is null) result.Add($"Inbound[{inbound.Name}].Binding", "references a missing outbound.");
                else if (!outbound.Enabled) result.Add($"Inbound[{inbound.Name}].Binding", "references a disabled outbound.");
            }
        }

        foreach (var binding in server.Bindings.Where(binding => binding.Enabled && !enabledInbounds.Any(inbound => inbound.Id == binding.InboundId)))
            result.Add("Bindings", "references a missing or disabled inbound.");
    }

    private static void ValidateBlockRules(ServerProfile server, ValidationResult result)
    {
        foreach (var rule in server.BlockRules.Where(rule => rule.Enabled))
        {
            var path = $"BlockRule[{rule.Name}]";
            if (rule.Id == Guid.Empty) result.Add(path, "has an empty id.");
            if (string.IsNullOrWhiteSpace(rule.Name)) result.Add(path + ".Name", "is required.");
            if (rule.Network is null) result.Add(path + ".Network", "is required.");
            if (rule.Ports.Count == 0) result.Add(path + ".Ports", "must contain at least one destination port or port range.");
            if (rule.InboundId is not null && !server.Inbounds.Any(inbound => inbound.Id == rule.InboundId.Value)) result.Add(path + ".InboundId", "references a missing inbound.");
            foreach (var range in rule.Ports)
                if (range.Start is < 1 or > 65535 || range.End is < 1 or > 65535 || range.Start > range.End) result.Add(path + ".Ports", "contains an invalid port range.");
        }
    }

    private static bool HostAndPort(string value)
    {
        var separator = value.LastIndexOf(':');
        return separator > 0 && int.TryParse(value[(separator + 1)..], out var port) && port is >= 1 and <= 65535;
    }

    private static bool IsHex(string value) => !string.IsNullOrWhiteSpace(value) && value.All(Uri.IsHexDigit);

    private static bool IsCidr(string value)
    {
        var separator = value.LastIndexOf('/');
        return separator > 0 && IPAddress.TryParse(value[..separator], out _) && int.TryParse(value[(separator + 1)..], out var prefix) && prefix is >= 0 and <= 128;
    }

    private static bool IsDnsDomain(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains('.', StringComparison.Ordinal) &&
        Uri.CheckHostName(value) == UriHostNameType.Dns;

    private static string? GetListenAddress(InboundProfile inbound) => inbound switch
    {
        RealityInboundProfile reality => reality.ListenAddress,
        Hysteria2InboundProfile hysteria => hysteria.ListenAddress,
        _ => null
    };

    private static NetworkProtocol GetTransport(InboundProfile inbound) => inbound switch
    {
        RealityInboundProfile => NetworkProtocol.Tcp,
        Hysteria2InboundProfile => NetworkProtocol.Udp,
        _ => NetworkProtocol.Tcp
    };
}
