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

    public async Task<TestPeer> CreatePeerAsync(string name, bool useBootstrap = true, string? testDataName = null, Func<string, byte[]?>? contentProvider = null)
    {
        var peer = TestPeerFactory.CreatePeer(name);
        _peers.Add(peer);

        if (testDataName != null) TestPeerFactory.InitializeWithTestData(peer, testDataName);

        List<string>? bootstrapNodes = null;
        if (useBootstrap)
        {
            if (_bootstrap == null)
            {
                BootstrapPort = TestPeerFactory.FindFreePort();
                _bootstrap = new BootstrapCoordinator(BootstrapPort, peer.Logger);
                await _bootstrap.StartAsync();
            }
            bootstrapNodes = [$"127.0.0.1:{BootstrapPort}"];
        }

        await peer.StartAsync(bootstrapNodes: bootstrapNodes, contentProvider: contentProvider);

        // Ensure they have a profile broadcasted so their public key is in the social manifest
        peer.BroadcastProfile(name, isArtist: true);

        return peer;
    }

    public async Task ConnectAndSyncAllAsync(int timeoutMs = 60000)
    {
        // First, ensure all peers are started and have a chance to talk to bootstrap
        foreach (var peer in _peers) await peer.SyncAsync();

        // Wait until they see each other in routing table
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs / 2);
        while (DateTime.UtcNow < deadline)
        {
            var allConnected = true;
            foreach (var peer in _peers)
                if (peer.Orchestrator.GetPeers().Count() < _peers.Count - 1)
                {
                    allConnected = false;
                    break;
                }

            if (allConnected) break;
            await Task.Delay(500, TestContext.Current.CancellationToken);
            foreach (var peer in _peers) await peer.SyncAsync();
        }

        // Now force a final exchange of all manifests
        foreach (var peer in _peers)
        foreach (var other in _peers)
        {
            if (peer == other) continue;

            var client = new ManifestExchangeClient(timeoutMs: 2000);
            var peerInfo = new PeerInfo
            {
                UserId = peer.UserId,
                DisplayName = peer.Name,
                Address = "127.0.0.1",
                Port = peer.Port,
                PublicKeyPem = peer.Identity.PublicKeyPem,
                LastSeen = DateTime.UtcNow
            };

            foreach (ManifestStreamType st in Enum.GetValues(typeof(ManifestStreamType)))
            {
                var manifest = peer.GetLocalManifest(st);
                if (manifest != null) await client.PushManifestAsync("127.0.0.1", other.Port, manifest, peerInfo);
            }
        }

        // Give it a moment to process the pushes
        await Task.Delay(1000, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var peer in _peers)
        {
            await peer.DisposeAsync();
            try {
                if (Directory.Exists(peer.BaseFolder)) Directory.Delete(peer.BaseFolder, true);
            } catch { }
        }

        if (_bootstrap != null)
        {
            await _bootstrap.StopAsync();
            _bootstrap.Dispose();
        }
    }
}
