using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class WireGuardConfigImporterTests
{
    [Fact]
    public void ImportsStandardClientConfiguration()
    {
        const string config = """
            [Interface]
            PrivateKey = private
            Address = 10.0.0.2/32, fd00::2/128
            MTU = 1280

            [Peer]
            PublicKey = public
            PresharedKey = shared
            Endpoint = wg.example.com:51820
            AllowedIPs = 0.0.0.0/0, ::/0
            PersistentKeepalive = 25
            """;

        var profile = new WireGuardConfigImporter().Import(config, "US WireGuard", "wg-us");

        Assert.Equal("wg.example.com", profile.EndpointHost);
        Assert.Equal(51820, profile.EndpointPort);
        Assert.Equal(["10.0.0.2/32", "fd00::2/128"], profile.LocalAddresses);
        Assert.Equal(25, profile.PersistentKeepalive);
    }
}
