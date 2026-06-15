using MeshWave.Common.Core.Crypto;
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
        await discovery.StartDiscoveryAsync(CreateTestIdentity(), cancellationToken: TestContext.Current.CancellationToken);
        await discovery.StopDiscoveryAsync();
    }

    [Fact]
    public async Task StopDiscoveryAsync_ExecutesWithoutError()
    {
        using var discovery = new PeerDiscovery(39991);
        await discovery.StartDiscoveryAsync(CreateTestIdentity(), cancellationToken: TestContext.Current.CancellationToken);
        await discovery.StopDiscoveryAsync();
    }

    [Fact]
    public void GetDiscoveredPeers_ReturnsEmptyListInitially()
    {
        using var discovery = new PeerDiscovery(39992);
        var peers = discovery.GetDiscoveredPeers().ToList();
        Assert.Empty(peers);
    }

    [Fact]
    public async Task Discovery_CanFindAnotherPeerOnLan()
    {
        var port = 39993; // Use a distinct port
        using var peer1 = new PeerDiscovery(port);
        using var peer2 = new PeerDiscovery(port);

        var identity1 = CreateTestIdentity();
        identity1.DisplayName = "Alice";

        var identity2 = CreateTestIdentity();
        identity2.DisplayName = "Bob";

        var tcs = new TaskCompletionSource<bool>();
        peer1.PeerDiscovered += (s, p) =>
        {
            if (p.UserId == identity2.UserId)
            {
                tcs.TrySetResult(true);
            }
        };

        await peer1.StartDiscoveryAsync(identity1, cancellationToken: TestContext.Current.CancellationToken);
        await peer2.StartDiscoveryAsync(identity2, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for peer1 to discover peer2 via UDP broadcast
        var discoveredTask = await Task.WhenAny(tcs.Task, Task.Delay(5000, TestContext.Current.CancellationToken));

        Assert.True(discoveredTask == tcs.Task && await tcs.Task, "Peer 1 did not discover Peer 2 on LAN");

        await peer1.StopDiscoveryAsync();
        await peer2.StopDiscoveryAsync();
    }
}
