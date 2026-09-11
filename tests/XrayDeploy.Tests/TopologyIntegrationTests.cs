using System.Text.Json.Nodes;
using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class TopologyIntegrationTests
{
    [Fact]
    public void SupportsMultipleInboundsWithIndependentImmediateExits()
    {
        var server = RealityConfigCompilerTests.CreateValidServer();
        var reality = server.Inbounds.OfType<RealityInboundProfile>().Single();
        var hy2 = new Hysteria2InboundProfile
        {
            Name = "JP HY2",
            Tag = "hy2-jp",
            ListenPort = 2053,
            CertificateDomain = "proxy.example.com",
            TlsCertificatePath = "/etc/ssl/cert.pem",
            TlsPrivateKeyPath = "/etc/ssl/key.pem"
        };
        hy2.Users.Add(new("bob", "hy2-auth"));
        var wireGuard = new WireGuardOutboundProfile
        {
            Name = "US WG", Tag = "wg-us", EndpointHost = "wg.example.com", EndpointPort = 51820,
            PrivateKey = "private", PeerPublicKey = "peer"
        };
        wireGuard.LocalAddresses.Add("10.0.0.2/32");
        wireGuard.AllowedIPs.Add("0.0.0.0/0");
        var importedReality = new ManagedServerOutboundImporter().CreateRealityOutbound(reality, reality.Users.Single(), "ph.example.com", "PH Reality", "reality-ph");
        server.Inbounds.Add(hy2);
        server.Outbounds.Add(importedReality);
        server.Outbounds.Add(wireGuard);
        server.Bindings.Clear();
        server.Bindings.Add(new RouteBinding { InboundId = reality.Id, Destination = new OutboundDestination(importedReality.Id) });
        server.Bindings.Add(new RouteBinding { InboundId = hy2.Id, Destination = new OutboundDestination(wireGuard.Id) });
        server.BlockRules.Add(new BlockRule { Name = "Block STUN", Network = NetworkProtocol.Udp, Ports = { new(3478, 3478) } });

        var config = JsonNode.Parse(new RealityConfigCompiler().Compile(server))!;

        Assert.Equal(2, config["inbounds"]!.AsArray().Count);
        Assert.Contains(config["outbounds"]!.AsArray(), outbound => outbound!["tag"]!.GetValue<string>() == "reality-ph");
        Assert.Contains(config["outbounds"]!.AsArray(), outbound => outbound!["tag"]!.GetValue<string>() == "wg-us");
        Assert.Equal("xraydeploy-block", config["routing"]!["rules"]![0]!["outboundTag"]!.GetValue<string>());
    }
}
