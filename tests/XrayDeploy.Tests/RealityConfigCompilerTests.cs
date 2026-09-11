using System.Text.Json.Nodes;
using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class RealityConfigCompilerTests
{
    [Fact]
    public void CompilesRealityInboundAndDirectRouteWithoutExposingTerminalAsProfile()
    {
        var server = CreateValidServer();
        var json = JsonNode.Parse(new RealityConfigCompiler().Compile(server))!;

        var inbound = json["inbounds"]![0]!;
        Assert.Equal("vless", inbound["protocol"]!.GetValue<string>());
        Assert.Equal("reality", inbound["streamSettings"]!["security"]!.GetValue<string>());
        Assert.Equal("tcp", inbound["streamSettings"]!["network"]!.GetValue<string>());
        Assert.Equal("www.cloudflare.com:443", inbound["streamSettings"]!["realitySettings"]!["target"]!.GetValue<string>());
        Assert.Equal("none", inbound["streamSettings"]!["tcpSettings"]!["header"]!["type"]!.GetValue<string>());
        Assert.Null(inbound["clientStats"]);
        Assert.Equal("freedom", json["outbounds"]![0]!["protocol"]!.GetValue<string>());
        Assert.Equal("xraydeploy-direct", json["routing"]!["rules"]![0]!["outboundTag"]!.GetValue<string>());
    }

    [Fact]
    public void RejectsEnabledInboundWithoutExactlyOneBinding()
    {
        var server = CreateValidServer();
        server.Bindings.Clear();

        var error = Assert.Throws<InvalidOperationException>(() => new RealityConfigCompiler().Compile(server));

        Assert.Contains("exactly one enabled primary exit", error.Message);
    }

    [Fact]
    public void ExportsPortableVlessRealityUri()
    {
        var profile = CreateValidServer().Inbounds.OfType<RealityInboundProfile>().Single();
        var export = new RealityClientExporter().Export(profile, profile.Users.Single(), "203.0.113.10", "SG Main");

        Assert.StartsWith($"vless://{profile.Users[0].Id:D}@203.0.113.10:443?", export.VlessUri);
        Assert.Contains("security=reality", export.VlessUri);
        Assert.Contains("#SG%20Main", export.VlessUri);
    }

    internal static ServerProfile CreateValidServer()
    {
        var inbound = new RealityInboundProfile
        {
            Name = "SG Reality",
            Tag = "reality-sg",
            ListenPort = 443,
            PrivateKey = "private-key",
            PublicKey = "public-key"
        };
        inbound.Users.Add(new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice"));
        inbound.ServerNames.Add("www.cloudflare.com");
        inbound.ShortIds.Add("a1b2c3d4");
        var server = new ServerProfile { Name = "Singapore" };
        server.Inbounds.Add(inbound);
        server.Bindings.Add(new RouteBinding { InboundId = inbound.Id, Destination = new DirectDestination() });
        return server;
    }
}
