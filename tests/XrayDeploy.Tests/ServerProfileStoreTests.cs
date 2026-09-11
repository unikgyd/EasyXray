using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class ServerProfileStoreTests
{
    [Fact]
    public async Task RoundTripsTheStrictTopologyDiscriminators()
    {
        var server = RealityConfigCompilerTests.CreateValidServer();
        server.BlockRules.Add(PrivacyPresets.CreateCommonStunUdpRule());
        var path = Path.Combine(Path.GetTempPath(), $"xraydeploy-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ServerProfileStore();
            await store.SaveAsync(server, path);
            var restored = await store.LoadAsync(path);
            Assert.IsType<RealityInboundProfile>(restored.Inbounds.Single());
            Assert.IsType<DirectDestination>(restored.Bindings.Single().Destination);
            Assert.Single(restored.BlockRules);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
