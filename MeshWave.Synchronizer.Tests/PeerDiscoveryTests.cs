using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Crypto;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class PeerDiscoveryTests
{
    private static LocalPeerIdentity CreateTestIdentity()
    {
        var (priv, pub) = CryptoService.GenerateKeyPair();
        return new LocalPeerIdentity
        {
            UserId = CryptoService.DeriveUserIdFromPublicKey(pub),
            DisplayName = "TestPeer",
            PublicKeyPem = pub,
            PrivateKeyPem = priv
        };
    }

    [Fact]
    public async Task StartDiscoveryAsync_ExecutesWithoutError()
    {
        using var discovery = new PeerDiscovery(39990);
        await discovery.StartDiscoveryAsync(CreateTestIdentity());
        await discovery.StopDiscoveryAsync();
    }

    [Fact]
    public async Task StopDiscoveryAsync_ExecutesWithoutError()
    {
        using var discovery = new PeerDiscovery(39991);
        await discovery.StartDiscoveryAsync(CreateTestIdentity());
        await discovery.StopDiscoveryAsync();
    }

    [Fact]
    public void GetDiscoveredPeers_ReturnsEmptyListInitially()
    {
        using var discovery = new PeerDiscovery(39992);
        var peers = discovery.GetDiscoveredPeers().ToList();
        Assert.Empty(peers);
    }
}

