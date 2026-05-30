using System.Net;
using System.Net.Sockets;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Integration.Tests;

/// <summary>
/// Integration tests that spin up real SyncOrchestrator instances on localhost
/// to verify mesh stability and data exchange between multiple simulated users.
///
/// Each test creates isolated peers on dynamic ports so tests do not conflict.
/// LAN UDP broadcast is disabled by using a stub PeerDiscovery; peers connect
/// directly via bootstrap node or manual PEX so no network privileges are needed.
/// </summary>
public class MeshIntegrationTests : IAsyncLifetime
{
    // Track all orchestrators/stores created per test so they are cleaned up.
    private readonly List<SyncOrchestrator> _orchestrators = [];
    private readonly List<string> _tempDirs = [];

    // ---------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var o in _orchestrators)
        {
            try { await o.StopAsync(); } catch { }
            o.Dispose();
        }
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ---------------------------------------------------------------------------
    // Helper: build an isolated SyncOrchestrator on a unique free port
    // ---------------------------------------------------------------------------

    private (SyncOrchestrator orchestrator, LocalPeerIdentity identity, Manifest manifest, int port)
        CreatePeer(string displayName)
    {
        var port = FindFreePort();
        var tempDir = Path.Combine(Path.GetTempPath(), $"mw_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        var storeDir = Path.Combine(tempDir, "store");

        // Use a NullPeerDiscovery to suppress UDP broadcast during tests.
        var discovery = new NullPeerDiscovery();
        var peerRouter = new PeerRouter(lanDiscovery: discovery);
        var server = new ManifestExchangeServer(port);
        var client = new ManifestExchangeClient(timeoutMs: 5_000);
        var mgr = new ManifestManager();
        var store = new PeerManifestStore(storeDir);

        var orchestrator = new SyncOrchestrator(peerRouter, server, client, mgr, store);
        _orchestrators.Add(orchestrator);

        var (privKey, pubKey) = CryptoService.GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pubKey);

        var identity = new LocalPeerIdentity
        {
            UserId = userId,
            DisplayName = displayName,
            PublicKeyPem = pubKey,
            PrivateKeyPem = privKey
        };

        var manifest = new Manifest
        {
            UserId = userId,
            Operations = [],
            Version = 1,
            LastUpdated = DateTime.UtcNow
        };

        return (orchestrator, identity, manifest, port);
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---------------------------------------------------------------------------
    // Test 1: Bootstrap node distributes peer list to late joiners
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Bootstrap_LateJoiner_CanDiscoverExistingPeer()
    {
        // Arrange: peer A acts as the bootstrap node.
        var (peerA, identityA, manifestA, portA) = CreatePeer("Alice");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");

        await peerA.StartAsync(identityA, manifestA);
        await peerB.StartAsync(identityB, manifestB,
            bootstrapNodes: [$"127.0.0.1:{portA}"]);

        // Allow bootstrap contact and manifest fetch.
        await WaitUntilAsync(() => peerB.PeerManifests.Count > 0 || peerB.ConnectedPeerCount > 0,
            timeoutMs: 5_000);

        // Act: late joiner C connects via the same bootstrap.
        var (peerC, identityC, manifestC, portC) = CreatePeer("Carol");
        await peerC.StartAsync(identityC, manifestC,
            bootstrapNodes: [$"127.0.0.1:{portA}"]);

        await WaitUntilAsync(() => peerC.ConnectedPeerCount > 0, timeoutMs: 5_000);

        // Assert: C can see at least the bootstrap node (A).
        Assert.True(peerC.ConnectedPeerCount > 0,
            "Late joiner should discover at least one peer via bootstrap.");
    }

    // ---------------------------------------------------------------------------
    // Test 2: Bootstrap restart – peers re-contact and recover routing table
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Bootstrap_Restart_PeersReconnectAfterRestart()
    {
        // Arrange: spin up bootstrap node A, connect B.
        var (bootstrap, identityBs, manifestBs, bsPort) = CreatePeer("BootstrapNode");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");

        await bootstrap.StartAsync(identityBs, manifestBs);
        await peerB.StartAsync(identityB, manifestB,
            bootstrapNodes: [$"127.0.0.1:{bsPort}"]);

        await WaitUntilAsync(() => peerB.ConnectedPeerCount > 0, timeoutMs: 5_000);
        Assert.True(peerB.ConnectedPeerCount > 0, "B should have found the bootstrap node.");

        // Simulate bootstrap restart by stopping and restarting it.
        await bootstrap.StopAsync();
        _orchestrators.Remove(bootstrap);
        bootstrap.Dispose();

        // Re-use same identity/manifest/port for the "restarted" bootstrap.
        var (bootstrap2, _, _, _) = CreatePeer("BootstrapNode");
        // We need to start on the same port – rebuild manually.
        _orchestrators.Remove(bootstrap2);
        bootstrap2.Dispose();
        _tempDirs.RemoveAt(_tempDirs.Count - 1);

        var tempDir2 = Path.Combine(Path.GetTempPath(), $"mw_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir2);
        _tempDirs.Add(tempDir2);

        var discovery2 = new NullPeerDiscovery();
        var router2 = new PeerRouter(lanDiscovery: discovery2);
        var server2 = new ManifestExchangeServer(bsPort);
        var bs2 = new SyncOrchestrator(router2, server2, new ManifestExchangeClient(5_000),
            new ManifestManager(), new PeerManifestStore(Path.Combine(tempDir2, "store")));
        _orchestrators.Add(bs2);

        await bs2.StartAsync(identityBs, manifestBs);

        // B should re-contact the bootstrap on its next maintenance cycle;
        // in tests we trigger a manual SyncAllPeersAsync to simulate the retry.
        await peerB.SyncAllPeersAsync();

        await WaitUntilAsync(() => peerB.ConnectedPeerCount > 0, timeoutMs: 5_000);
        Assert.True(peerB.ConnectedPeerCount > 0,
            "Peer B should recover connectivity after bootstrap restarts.");
    }

    // ---------------------------------------------------------------------------
    // Test 3: Manifest exchange – track announcement propagates to peers
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManifestExchange_TrackAnnouncement_PropagatesAcrossNetwork()
    {
        // Arrange: 3 peers all bootstrapped via peer A.
        var (peerA, identityA, manifestA, portA) = CreatePeer("Alice");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");
        var (peerC, identityC, manifestC, portC) = CreatePeer("Carol");

        await peerA.StartAsync(identityA, manifestA);
        await peerB.StartAsync(identityB, manifestB, bootstrapNodes: [$"127.0.0.1:{portA}"]);
        await peerC.StartAsync(identityC, manifestC, bootstrapNodes: [$"127.0.0.1:{portA}"]);

        await Task.Delay(1_000); // let routing tables settle

        // Act: A announces a track.
        peerA.AnnounceTrack("track-001", "abc123hash", new Dictionary<string, string>
        {
            ["title"] = "Test Song",
            ["artist"] = "Alice"
        });

        // Trigger syncs so B and C pull A's manifest.
        await peerB.SyncAllPeersAsync();
        await peerC.SyncAllPeersAsync();

        await WaitUntilAsync(
            () => peerB.GetPeerManifest(identityA.UserId)?.Operations.Count > 0,
            timeoutMs: 5_000);
        await WaitUntilAsync(
            () => peerC.GetPeerManifest(identityA.UserId)?.Operations.Count > 0,
            timeoutMs: 5_000);

        // Assert: both B and C received Alice's Create operation.
        var bCopy = peerB.GetPeerManifest(identityA.UserId);
        var cCopy = peerC.GetPeerManifest(identityA.UserId);

        Assert.NotNull(bCopy);
        Assert.Contains(bCopy.Operations, op =>
            op.OperationType == ManifestOperationType.Create &&
            op.TargetId == "track-001" &&
            op.ContentHash == "abc123hash");

        Assert.NotNull(cCopy);
        Assert.Contains(cCopy.Operations, op =>
            op.OperationType == ManifestOperationType.Create &&
            op.TargetId == "track-001");
    }

    // ---------------------------------------------------------------------------
    // Test 4: Profile broadcast propagates to peers
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManifestExchange_ProfileBroadcast_PropagatesAcrossNetwork()
    {
        var (peerA, identityA, manifestA, portA) = CreatePeer("Alice");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");

        await peerA.StartAsync(identityA, manifestA);
        await peerB.StartAsync(identityB, manifestB, bootstrapNodes: [$"127.0.0.1:{portA}"]);

        await Task.Delay(500);

        // Alice broadcasts her artist profile.
        peerA.BroadcastProfile("Alice Artist", isArtist: true, "My bio", "https://alice.example", null);

        await peerB.SyncAllPeersAsync();

        await WaitUntilAsync(
            () => peerB.GetPeerManifest(identityA.UserId)?.Operations
                       .Any(op => op.OperationType == ManifestOperationType.Profile) == true,
            timeoutMs: 5_000);

        var manifest = peerB.GetPeerManifest(identityA.UserId);
        Assert.NotNull(manifest);

        var profileOp = manifest.Operations.FirstOrDefault(op => op.OperationType == ManifestOperationType.Profile);
        Assert.NotNull(profileOp);
        Assert.Equal("Alice Artist", profileOp.Metadata["displayName"]);
        Assert.Equal("True", profileOp.Metadata["isArtist"]);
    }

    // ---------------------------------------------------------------------------
    // Test 5: Follow / Unfollow operations exchange correctly
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManifestExchange_FollowUnfollow_PropagatesCorrectly()
    {
        var (peerA, identityA, manifestA, portA) = CreatePeer("Alice");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");

        await peerA.StartAsync(identityA, manifestA);
        await peerB.StartAsync(identityB, manifestB, bootstrapNodes: [$"127.0.0.1:{portA}"]);

        await Task.Delay(500);

        // Bob follows then unfollows Alice.
        peerB.RecordFollow(identityA.UserId);
        peerB.RecordUnfollow(identityA.UserId);

        // Alice pulls Bob's manifest.
        await peerA.SyncAllPeersAsync();

        await WaitUntilAsync(
            () => peerA.GetPeerManifest(identityB.UserId)?.Operations.Count >= 2,
            timeoutMs: 5_000);

        var bobManifest = peerA.GetPeerManifest(identityB.UserId);
        Assert.NotNull(bobManifest);
        Assert.Contains(bobManifest.Operations, op => op.OperationType == ManifestOperationType.Follow);
        Assert.Contains(bobManifest.Operations, op => op.OperationType == ManifestOperationType.Unfollow);
    }

    // ---------------------------------------------------------------------------
    // Test 6: ManifestMerged event fires when peer data arrives
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManifestMerged_Event_FiresWhenPeerDataArrives()
    {
        var (peerA, identityA, manifestA, portA) = CreatePeer("Alice");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");

        // A announces content before B connects.
        var mergeEvents = new List<ManifestMergedEventArgs>();
        peerB.ManifestMerged += (_, args) => mergeEvents.Add(args);

        await peerA.StartAsync(identityA, manifestA);
        peerA.AnnounceTrack("track-alpha", "hashAlpha");

        await peerB.StartAsync(identityB, manifestB, bootstrapNodes: [$"127.0.0.1:{portA}"]);
        await peerB.SyncAllPeersAsync();

        await WaitUntilAsync(() => mergeEvents.Count > 0, timeoutMs: 5_000);

        Assert.NotEmpty(mergeEvents);
        Assert.Contains(mergeEvents, e => e.UserId == identityA.UserId && e.OperationsAdded > 0);
    }

    // ---------------------------------------------------------------------------
    // Test 7: Mesh stability – signatures from tampered manifests are rejected
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManifestExchange_TamperedManifest_IsRejected()
    {
        var (peerA, identityA, manifestA, portA) = CreatePeer("Alice");
        var (peerB, identityB, manifestB, portB) = CreatePeer("Bob");

        await peerA.StartAsync(identityA, manifestA);

        // Manually push a tampered manifest from "Bob" to Alice using Bob's identity
        // but with a content hash swapped post-signing.
        var mgr = new ManifestManager();
        var fakeManifest = mgr.CreateManifest(identityB.UserId);
        mgr.AppendSignedOperation(fakeManifest, ManifestOperationType.Create,
            "evil-track", "Track", "legit-hash", null, identityB.PrivateKeyPem);

        // Tamper: change content hash after signing.
        fakeManifest.Operations[0].ContentHash = "tampered-hash";

        var client = new ManifestExchangeClient(timeoutMs: 5_000);
        await peerA.StartAsync(identityA, manifestA); // already running; no-op start is fine

        await peerB.StartAsync(identityB, manifestB, bootstrapNodes: [$"127.0.0.1:{portA}"]);
        await Task.Delay(300);

        // Push tampered manifest directly to A.
        var pushed = await client.PushManifestAsync("127.0.0.1", portA, fakeManifest);

        // Allow time for A to process.
        await Task.Delay(500);

        // A should have rejected it — no ops from Bob stored (signature mismatch).
        var stored = peerA.GetPeerManifest(identityB.UserId);
        var hasEvilTrack = stored?.Operations.Any(op => op.ContentHash == "tampered-hash") == true;
        Assert.False(hasEvilTrack, "Tampered manifest operation must be rejected by signature verification.");
    }

    // ---------------------------------------------------------------------------
    // Utility
    // ---------------------------------------------------------------------------

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(100);
    }
}
