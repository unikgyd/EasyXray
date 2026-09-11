using System.Text.Json.Nodes;
using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class ProtocolCompilerTests
{
    [Fact]
    public void CompilesHysteria2InboundWithTlsAndUserAuthentication()
    {
        var profile = new Hysteria2InboundProfile
        {
            Name = "HY2", Tag = "hy2-in", ListenPort = 2053, CertificateDomain = "hy2.example.com",
            TlsCertificatePath = "/etc/xray/cert.pem", TlsPrivateKeyPath = "/etc/xray/key.pem",
            Obfuscation = "obfs-password"
        };
        profile.Users.Add(new("alice", "auth-value"));
        var server = new ServerProfile { Name = "server" };
        server.Inbounds.Add(profile);
        server.Bindings.Add(new RouteBinding { InboundId = profile.Id, Destination = new DirectDestination() });

        var config = JsonNode.Parse(new RealityConfigCompiler().Compile(server))!;
        var inbound = config["inbounds"]![0]!;

        Assert.Equal("hysteria", inbound["protocol"]!.GetValue<string>());
        Assert.Equal("auth-value", inbound["settings"]!["users"]![0]!["auth"]!.GetValue<string>());
        Assert.Equal("hysteria", inbound["streamSettings"]!["network"]!.GetValue<string>());
        Assert.Equal(2, inbound["streamSettings"]!["hysteriaSettings"]!["version"]!.GetValue<int>());
        Assert.Equal(60, inbound["streamSettings"]!["hysteriaSettings"]!["udpIdleTimeout"]!.GetValue<int>());
        Assert.Null(inbound["streamSettings"]!["hysteriaSettings"]!["auth"]);
        Assert.Equal("hy2.example.com", inbound["streamSettings"]!["tlsSettings"]!["serverName"]!.GetValue<string>());
        Assert.Equal("h3", inbound["streamSettings"]!["tlsSettings"]!["alpn"]![0]!.GetValue<string>());
        Assert.Equal("/etc/xray/cert.pem", inbound["streamSettings"]!["tlsSettings"]!["certificates"]![0]!["certificateFile"]!.GetValue<string>());
        Assert.Equal("salamander", inbound["streamSettings"]!["finalmask"]!["udp"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("obfs-password", inbound["streamSettings"]!["finalmask"]!["udp"]![0]!["settings"]!["password"]!.GetValue<string>());
        Assert.Null(inbound["clientStats"]);
    }

    [Fact]
    public void CompilesWireGuardOnlyAsAnOutboundPath()
    {
        var server = RealityConfigCompilerTests.CreateValidServer();
        var wireGuard = new WireGuardOutboundProfile
        {
            Name = "WG", Tag = "wg-out", EndpointHost = "wg.example.com", EndpointPort = 51820,
            PrivateKey = "private", PeerPublicKey = "public", PersistentKeepalive = 25, Mtu = 1280
        };
        wireGuard.LocalAddresses.Add("10.0.0.2/32");
        wireGuard.AllowedIPs.Add("0.0.0.0/0");
        server.Outbounds.Add(wireGuard);
        server.Bindings.Clear();
        server.Bindings.Add(new RouteBinding { InboundId = server.Inbounds.Single().Id, Destination = new OutboundDestination(wireGuard.Id) });

        var config = JsonNode.Parse(new RealityConfigCompiler().Compile(server))!;
        var outbound = config["outbounds"]!.AsArray().Single(item => item!["tag"]!.GetValue<string>() == "wg-out")!;

        Assert.Equal("wireguard", outbound["protocol"]!.GetValue<string>());
        Assert.Equal("wg.example.com:51820", outbound["settings"]!["peers"]![0]!["endpoint"]!.GetValue<string>());
        Assert.Equal(25, outbound["settings"]!["peers"]![0]!["keepAlive"]!.GetValue<int>());
        Assert.DoesNotContain(config["inbounds"]!.AsArray(), item => item!["protocol"]!.GetValue<string>() == "wireguard");
    }

    [Fact]
    public void RejectsInvalidBlockPortRangeAndConflictingListeners()
    {
        var server = RealityConfigCompilerTests.CreateValidServer();
        var duplicate = new RealityInboundProfile
        {
            Name = "duplicate", Tag = "duplicate", ListenPort = 443,
            PrivateKey = "private", PublicKey = "public"
        };
        duplicate.Users.Add(new(Guid.NewGuid(), "user")); duplicate.ServerNames.Add("example.com"); duplicate.ShortIds.Add("12");
        server.Inbounds.Add(duplicate);
        server.BlockRules.Add(new BlockRule { Name = "bad", Network = NetworkProtocol.Udp, Ports = { new(3480, 3478) } });
        server.Bindings.Add(new RouteBinding { InboundId = duplicate.Id, Destination = new DirectDestination() });

        var validation = new RealityTopologyValidator().Validate(server);

        Assert.Contains(validation.Issues, issue => issue.Message.Contains("configured more than once", StringComparison.Ordinal));
        Assert.Contains(validation.Issues, issue => issue.Message.Contains("invalid port range", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowsRealityTcpAndHysteriaUdpOnPort443ButRequiresHysteriaDomain()
    {
        var server = RealityConfigCompilerTests.CreateValidServer();
        var hysteria = new Hysteria2InboundProfile
        {
            Name = "HY2", Tag = "hy2-443", ListenPort = 443,
            CertificateDomain = "hy2.example.com",
            TlsCertificatePath = "/etc/letsencrypt/live/hy2.example.com/fullchain.pem",
            TlsPrivateKeyPath = "/etc/letsencrypt/live/hy2.example.com/privkey.pem"
        };
        hysteria.Users.Add(new("user", "auth"));
        server.Inbounds.Add(hysteria);
        server.Bindings.Add(new RouteBinding { InboundId = hysteria.Id, Destination = new DirectDestination() });

        var valid = new RealityTopologyValidator().Validate(server);
        hysteria.CertificateDomain = "";
        var missingDomain = new RealityTopologyValidator().Validate(server);

        Assert.True(valid.IsValid);
        Assert.Contains(missingDomain.Issues, issue => issue.Path.EndsWith("CertificateDomain", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportsSecureHysteria2LinkWithCertificateDomain()
    {
        var inbound = new Hysteria2InboundProfile
        {
            Name = "HY2", Tag = "hy2", ListenPort = 443,
            CertificateDomain = "hy2.example.com",
            TlsCertificatePath = "/cert.pem", TlsPrivateKeyPath = "/key.pem"
        };
        var user = new Hysteria2User("player", "secret");
        inbound.Users.Add(user);

        var export = new Hysteria2ClientExporter().Export(inbound, user, "hy2.example.com");

        Assert.StartsWith("hysteria2://secret@hy2.example.com:443/", export.Hy2Uri);
        Assert.Contains("sni=hy2.example.com", export.Hy2Uri);
    }
}
