using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class PeerDiscoveryTests
{
    private readonly PeerDiscovery _peerDiscovery = new();

    [Fact]
    public async Task StartDiscoveryAsync_ExecutesWithoutError()
    {
        // Act & Assert - should not throw
        await _peerDiscovery.StartDiscoveryAsync();
    }

    [Fact]
    public async Task StopDiscoveryAsync_ExecutesWithoutError()
    {
        // Act & Assert - should not throw
        await _peerDiscovery.StopDiscoveryAsync();
    }

    [Fact]
    public void GetDiscoveredPeers_ReturnsEmptyListInitially()
    {
        // Act
        var peers = _peerDiscovery.GetDiscoveredPeers().ToList();

        // Assert
        Assert.Empty(peers);
    }
}
