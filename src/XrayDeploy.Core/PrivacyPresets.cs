namespace XrayDeploy.Core;

public static class PrivacyPresets
{
    // This affects STUN/WebRTC traffic that traverses this proxy path; it cannot block traffic sent directly by a client.
    public static BlockRule CreateCommonStunUdpRule(Guid? inboundId = null) => new()
    {
        Name = "Block common STUN/WebRTC UDP traffic",
        InboundId = inboundId,
        Network = NetworkProtocol.Udp,
        Ports = { new(3478, 3479), new(5349, 5349) }
    };
}
