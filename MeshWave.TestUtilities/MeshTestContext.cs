using MeshWave.Bootstrap.Core;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.TestUtilities;

/// <summary>
/// Main test harness for MeshWave P2P integration testing.
/// Manages isolated peer environments and an optional bootstrap node.
/// </summary>
public class MeshTestContext : IAsyncDisposable
{
    private readonly List<TestPeer> _peers = [];
    private BootstrapCoordinator? _bootstrap;

    public int BootstrapPort { get; private set; }

    public IReadOnlyList<TestPeer> Peers => _peers;

    /// <summary>
    /// Creates and starts a peer.
    /// </summary>
    /// <param name="useBootstrap">Register with the shared standalone bootstrap node (created on first use).</param>
    /// <param name="bootstrapNodes">Explicit bootstrap endpoints; overrides <paramref name="useBootstrap"/> (e.g. another peer acting as bootstrap).</param>
    /// <param name="actAsListener">False starts the peer outbound-only (no TCP listener), like a peer behind NAT without port forwarding.</param>
    public async Task<TestPeer> CreatePeerAsync(string name, bool useBootstrap = true, string? testDataName = null, Func<string, byte[]?>? contentProvider = null,
        IReadOnlyList<string>? bootstrapNodes = null, bool actAsListener = true)
    {
        var peer = TestPeerFactory.CreatePeer(name);
        _peers.Add(peer);

        if (testDataName != null) TestPeerFactory.InitializeWithTestData(peer, testDataName);

        if (bootstrapNodes == null && useBootstrap)
        {
            if (_bootstrap == null)
            {
                BootstrapPort = TestPeerFactory.FindFreePort();
                _bootstrap = new BootstrapCoordinator(BootstrapPort, peer.Logger);
                await _bootstrap.StartAsync();
            }
            bootstrapNodes = [$"127.0.0.1:{BootstrapPort}"];
        }

        await peer.StartAsync(bootstrapNodes: bootstrapNodes, actAsListener: actAsListener, contentProvider: contentProvider);

        // Ensure they have a profile broadcasted so their public key is in the social manifest
        peer.BroadcastProfile(name, isArtist: true);

        return peer;
    }

    /// <summary>
    /// Waits until every peer has discovered every other peer through the real discovery path
    /// (bootstrap announce + PEX + pushes) and holds its Social manifest. Nothing is pushed on the peers' behalf.
    /// Throws <see cref="TimeoutException"/> if the mesh does not converge, so tests fail on broken discovery.
    /// </summary>
    public async Task ConnectAndSyncAllAsync(int timeoutMs = 60000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            // Periodic sync normally runs every minute; trigger it directly to keep tests fast.
            foreach (var peer in _peers) await peer.SyncAsync();

            var missing = FindMissingLinks();
            if (missing.Count == 0) return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Mesh did not converge: " + string.Join("; ", missing));

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }
    }

    private List<string> FindMissingLinks()
    {
        var missing = new List<string>();
        foreach (var peer in _peers)
        {
            var known = peer.Orchestrator.GetPeers().Select(p => p.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var other in _peers.Where(o => o != peer))
            {
                if (!known.Contains(other.UserId))
                    missing.Add($"{peer.Name} has not discovered {other.Name}");
                else if (peer.GetPeerManifest(other.UserId, ManifestStreamType.Social) == null)
                    missing.Add($"{peer.Name} has no manifest from {other.Name}");
            }
        }
        return missing;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var peer in _peers)
        {
            await peer.DisposeAsync();
            try {
                if (Directory.Exists(peer.BaseFolder)) Directory.Delete(peer.BaseFolder, true);
            } catch { 
                // Ignore all
            }
        }

        if (_bootstrap != null)
        {
            await _bootstrap.StopAsync();
            _bootstrap.Dispose();
        }
    }
}
