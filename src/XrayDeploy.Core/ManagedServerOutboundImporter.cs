namespace XrayDeploy.Core;

public sealed class ManagedServerOutboundImporter
{
    public RealityOutboundProfile CreateRealityOutbound(RealityInboundProfile source, RealityUser user, string serverAddress, string name, string tag)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(user);
        if (!source.Users.Any(candidate => candidate.Id == user.Id)) throw new ArgumentException("The selected user does not belong to the source REALITY inbound.", nameof(user));
        return new RealityOutboundProfile
        {
            Name = name,
            Tag = tag,
            ServerAddress = serverAddress,
            ServerPort = source.ListenPort,
            UserId = user.Id,
            Flow = user.Flow,
            ServerName = source.ServerNames.FirstOrDefault() ?? throw new InvalidOperationException("The source REALITY inbound has no server name."),
            PublicKey = source.PublicKey,
            ShortId = source.ShortIds.FirstOrDefault() ?? throw new InvalidOperationException("The source REALITY inbound has no short id."),
            Fingerprint = "chrome"
        };
    }

    public Hysteria2OutboundProfile CreateHysteria2Outbound(Hysteria2InboundProfile source, Hysteria2User user, string serverAddress, string tlsServerName, string name, string tag)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(user);
        if (!source.Users.Contains(user)) throw new ArgumentException("The selected user does not belong to the source Hysteria2 inbound.", nameof(user));
        return new Hysteria2OutboundProfile
        {
            Name = name,
            Tag = tag,
            ServerAddress = serverAddress,
            ServerPort = source.ListenPort,
            Authentication = user.Authentication,
            TlsServerName = tlsServerName,
            Obfuscation = source.Obfuscation,
            UdpIdleTimeoutSeconds = source.UdpIdleTimeoutSeconds
        };
    }
}
