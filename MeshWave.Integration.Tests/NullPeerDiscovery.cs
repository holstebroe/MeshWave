using MeshWave.Synchronizer;

namespace MeshWave.Integration.Tests;

/// <summary>
/// A no-op PeerDiscovery stub for integration tests.
/// Suppresses all UDP broadcast traffic so tests run without network privileges
/// and do not interfere with each other.
/// </summary>
internal sealed class NullPeerDiscovery : PeerDiscovery
{
    public NullPeerDiscovery() : base(listenPort: 0) { }

    public override Task StartDiscoveryAsync(
        LocalPeerIdentity identity,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override Task StopDiscoveryAsync()
    {
        return Task.CompletedTask;
    }
}
