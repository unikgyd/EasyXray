using System.Text.Json.Nodes;
using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class BlockRuleTests
{
    [Fact]
    public void CompilesExplicitBlockRulesBeforePrimaryRoute()
    {
        var server = RealityConfigCompilerTests.CreateValidServer();
        server.BlockRules.Add(PrivacyPresets.CreateCommonStunUdpRule());

        var config = JsonNode.Parse(new RealityConfigCompiler().Compile(server))!;
        var rules = config["routing"]!["rules"]!.AsArray();

        Assert.Equal("xraydeploy-block", rules[0]!["outboundTag"]!.GetValue<string>());
        Assert.Equal("udp", rules[0]!["network"]!.GetValue<string>());
        Assert.Equal("xraydeploy-direct", rules[1]!["outboundTag"]!.GetValue<string>());
    }
}
