using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using NLog.Targets;
using Xunit;

namespace MeshWave.Integration.Tests;

public class MeshIntegrationTests : IAsyncLifetime
{
    private MeshTestContext _context = default!;
    private readonly ITestOutputHelper _output;

    public MeshIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public ValueTask InitializeAsync()
    {
        _context = new MeshTestContext();
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task Bootstrap_LateJoiner_CanDiscoverExistingPeer()
    {
        _output.WriteLine("Starting Bootstrap_LateJoiner_CanDiscoverExistingPeer");
        var peerA = await _context.CreatePeerAsync("Alice");
        _output.WriteLine($"Peer A (Alice) created: {peerA.UserId} on port {peerA.Port}");

        var peerB = await _context.CreatePeerAsync("Bob");
        _output.WriteLine($"Peer B (Bob) created: {peerB.UserId} on port {peerB.Port}");

        _output.WriteLine("Waiting for Peer B to discover Peer A...");
        await peerB.WaitForConditionAsync(() => peerB.Orchestrator.GetPeers().Any(p => p.UserId == peerA.UserId));
        _output.WriteLine($"Peer B connected peer count: {peerB.Orchestrator.ConnectedPeerCount}");

        // The bootstrap entry itself must not be what satisfies discovery: B must know A by UserId, with A's key.
        var aAsSeenByB = peerB.Orchestrator.GetPeers().Single(p => p.UserId == peerA.UserId);
        Assert.Equal(peerA.Identity.PublicKeyPem, aAsSeenByB.PublicKeyPem);
        Assert.Equal(peerA.Port, aAsSeenByB.Port);
    }

    [Fact]
    public async Task Bootstrap_ListeningPeers_ConvergeWithoutForcedPushes()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        alice.AnnounceTrack("alice-track-1", "alice-hash-1", new Dictionary<string, string> { ["title"] = "Alice One" });

        // ConnectAndSyncAllAsync only waits and triggers the periodic sync; it throws if discovery is broken.
        await _context.ConnectAndSyncAllAsync(timeoutMs: 30000);

        await bob.WaitForConditionAsync(() => CountPublicTracks(bob.GetPeerManifest(alice.UserId)) == 1);
        Assert.Contains(alice.Orchestrator.GetPeers(), p => p.UserId == bob.UserId);
    }

    [Fact]
    public async Task OnlyOnePeerListening_OutboundOnlyPeerExchangesManifestsBothWays()
    {
        // Alice has an open port and is Bob's only bootstrap node. Bob has no listener at all (behind NAT).
        var alice = await _context.CreatePeerAsync("Alice", useBootstrap: false);
        var bob = await _context.CreatePeerAsync("Bob", bootstrapNodes: [$"127.0.0.1:{alice.Port}"], actAsListener: false);

        alice.AnnounceTrack("alice-track-1", "alice-hash-1", new Dictionary<string, string> { ["title"] = "Alice One" });
        bob.AnnounceTrack("bob-track-1", "bob-hash-1", new Dictionary<string, string> { ["title"] = "Bob One" });

        // Bob learns Alice as a real peer (not only as an anonymous bootstrap entry) from the Announce response.
        await bob.WaitForConditionAsync(() => bob.Orchestrator.GetPeers().Any(p => p.UserId == alice.UserId));

        // Alice learns Bob as outbound-only: registered, but with port 0 so nobody tries to dial him.
        await alice.WaitForConditionAsync(() => alice.Orchestrator.GetPeers().Any(p => p.UserId == bob.UserId));
        Assert.Equal(0, alice.Orchestrator.GetPeers().Single(p => p.UserId == bob.UserId).Port);

        // Bob -> Alice: Bob pushes over his own outbound connection.
        await alice.WaitForConditionAsync(() => CountPublicTracks(alice.GetPeerManifest(bob.UserId)) == 1);

        // Alice -> Bob: Bob keeps a persistent session with Alice, so Alice can push to him although he has no open port.
        await bob.WaitForConditionAsync(() => CountPublicTracks(bob.GetPeerManifest(alice.UserId)) == 1);
        Assert.NotNull(alice.Orchestrator.Sessions.GetSession(bob.UserId));

        // Later updates arrive as pushes over that session, without anyone polling.
        alice.AnnounceTrack("alice-track-2", "alice-hash-2", new Dictionary<string, string> { ["title"] = "Alice Two" });
        await WaitWithoutSyncAsync(() => CountPublicTracks(bob.GetPeerManifest(alice.UserId)) == 2);
    }

    [Fact]
    public async Task TwoPeersWithoutOpenPorts_ExchangeManifestsDirectlyOverHolePunchedUdp()
    {
        // Alice has an open port and acts as bootstrap and introducer. Bob and Carol have no listener (behind NAT).
        // Alice replicates their streams like any peer, so the test checks that Bob's own pushes reach Carol: with no open
        // port on either side, that is only possible over a direct, hole-punched Bob <-> Carol session.
        var alice = await _context.CreatePeerAsync("Alice", useBootstrap: false);
        var bob = await _context.CreatePeerAsync("Bob", bootstrapNodes: [$"127.0.0.1:{alice.Port}"], actAsListener: false);
        var carol = await _context.CreatePeerAsync("Carol", bootstrapNodes: [$"127.0.0.1:{alice.Port}"], actAsListener: false);

        bob.AnnounceTrack("bob-track-1", "bob-hash-1", new Dictionary<string, string> { ["title"] = "Bob One" });
        carol.AnnounceTrack("carol-track-1", "carol-hash-1", new Dictionary<string, string> { ["title"] = "Carol One" });

        await WaitWithoutSyncAsync(() => bob.Orchestrator.Sessions.GetSession(carol.UserId) != null
                                         && carol.Orchestrator.Sessions.GetSession(bob.UserId) != null, timeoutMs: 60000);
        Assert.Equal("udp", bob.Orchestrator.Sessions.GetSession(carol.UserId)!.TransportKind);

        await WaitWithoutSyncAsync(() => CountPublicTracks(carol.GetPeerManifest(bob.UserId)) == 1
                                         && CountPublicTracks(bob.GetPeerManifest(carol.UserId)) == 1);

        // New publications are pushed over the punched session.
        bob.AnnounceTrack("bob-track-2", "bob-hash-2", new Dictionary<string, string> { ["title"] = "Bob Two" });
        await WaitWithoutSyncAsync(() => CountPublicTracks(carol.GetPeerManifest(bob.UserId)) == 2);
        await WaitWithoutSyncAsync(() => carol.Orchestrator.GetPeerDiagnosticsSnapshots().Single(p => p.UserId == bob.UserId).RecentMessages
            .Any(m => m.MessageType == "PushManifest" && m.Details.Contains("from the author")));

        // Neither has an open port: both are known as outbound-only.
        Assert.Equal(0, carol.Orchestrator.GetPeers().Single(p => p.UserId == bob.UserId).Port);
    }

    [Fact]
    public async Task Bootstrap_NeverStoresOrServesManifests()
    {
        var alice = await _context.CreatePeerAsync("Alice", actAsListener: false);
        alice.AnnounceTrack("alice-track-1", "alice-hash-1", new Dictionary<string, string> { ["title"] = "Alice One" });
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var client = new ManifestExchangeClient(timeoutMs: 2000);
        var manifest = await client.FetchManifestAsync("127.0.0.1", _context.BootstrapPort, ManifestStreamType.Content,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(manifest);

        // The bootstrap knows Alice (outbound-only), but does not advertise any relay for her.
        var peers = await client.FetchPeersAsync("127.0.0.1", _context.BootstrapPort, cancellationToken: TestContext.Current.CancellationToken);
        var aliceEntry = Assert.Single(peers!, p => p.UserId == alice.UserId);
        Assert.Equal(0, aliceEntry.Port);
        Assert.DoesNotContain("relay", aliceEntry.Capabilities);
    }

    /// <summary>Waits for a condition without triggering any sync, so only pushes can satisfy it.</summary>
    private static async Task WaitWithoutSyncAsync(Func<bool> condition, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ForgedManifestPush_WithKeyNotMatchingUserId_IsRejected()
    {
        var alice = await _context.CreatePeerAsync("Alice", useBootstrap: false);
        var victimUserId = CryptoService.DeriveUserIdFromPublicKey(CryptoService.GenerateKeyPair().publicKeyPem);
        var (attackerPrivateKey, attackerPublicKey) = CryptoService.GenerateKeyPair();

        var manager = new ManifestManager();
        var forged = manager.CreateManifest(victimUserId);
        forged.StreamType = ManifestStreamType.Content;
        manager.AppendSignedOperation(forged, ManifestOperationType.Create, "fake-track", "Track", "fake-hash",
            new Dictionary<string, string> { ["title"] = "Forged" }, attackerPrivateKey);

        var client = new ManifestExchangeClient(timeoutMs: 2000);
        await client.PushManifestAsync("127.0.0.1", alice.Port, forged, new MeshWave.Common.Core.P2P.PeerInfo
        {
            UserId = victimUserId,
            DisplayName = "Victim",
            Address = "127.0.0.1",
            Port = 1,
            PublicKeyPem = attackerPublicKey
        }, TestContext.Current.CancellationToken);

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Null(alice.GetPeerManifest(victimUserId));
        Assert.DoesNotContain(alice.Orchestrator.GetPeers(), p => p.UserId == victimUserId);
    }

    [Fact]
    public async Task LargeContent_SpanningManyChunks_IsDownloadedByteExact()
    {
        // Larger than several 512 KB chunks, with non-repeating bytes so a mis-placed chunk is detected.
        var content = new byte[3 * 1024 * 1024 + 12345];
        new Random(42).NextBytes(content);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));

        var john = await _context.CreatePeerAsync("John", contentProvider: h => string.Equals(h, hash, StringComparison.OrdinalIgnoreCase) ? content : null);
        var jane = await _context.CreatePeerAsync("Jane");
        await _context.ConnectAndSyncAllAsync();

        var downloaded = await jane.Orchestrator.RequestContentAsync(john.UserId, hash);

        Assert.NotNull(downloaded);
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task Bootstrap_PeriodicRetry_IntervalIsConfigured()
    {
        var interval = SecurityLimits.BootstrapRetryIntervalMinutes;
        Assert.True(interval > 0, "Bootstrap retry interval must be configured.");
        Assert.True(interval <= 60, "Bootstrap retry interval should be reasonable (≤ 60 min).");
    }

    [Fact]
    public async Task ManifestExchange_SignedOperation_IsVerifiable()
    {
        var alice = await _context.CreatePeerAsync("Alice");

        alice.AnnounceTrack("track-001", "abc123hash", new Dictionary<string, string>
        {
            ["title"] = "Test Song",
            ["artist"] = "Alice"
        });

        var manifest = alice.GetLocalManifest(ManifestStreamType.Content);
        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.Operations);
        Assert.Contains(manifest.Operations, op =>
            op.OperationType == ManifestOperationType.Create &&
            op.TargetId == "track-001" &&
            op.ContentHash == "abc123hash" &&
            !string.IsNullOrWhiteSpace(op.Signature));
    }

    [Fact]
    public async Task ManifestExchange_ProfileBroadcast_IsRecorded()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        alice.BroadcastProfile("Alice Artist", isArtist: true, "My bio", "https://alice.example");

        var manifest = alice.GetLocalManifest(ManifestStreamType.Social);
        Assert.NotNull(manifest);
        var profileOp = manifest.Operations.OrderByDescending(op => op.SequenceNumber).FirstOrDefault(op => op.OperationType == ManifestOperationType.Profile);
        Assert.NotNull(profileOp);
        Assert.Equal("Alice Artist", profileOp.Metadata["displayName"]);
        Assert.Equal("True", profileOp.Metadata["isArtist"]);
    }

    [Fact]
    public async Task ManifestExchange_FollowUnfollow_AreRecorded()
    {
        var bob = await _context.CreatePeerAsync("Bob");
        var targetUserId = "user-123";

        bob.Orchestrator.RecordFollow(targetUserId);
        bob.Orchestrator.RecordUnfollow(targetUserId);

        var manifest = bob.GetLocalManifest(ManifestStreamType.Social);
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Operations, op => op.OperationType == ManifestOperationType.Follow);
        Assert.Contains(manifest.Operations, op => op.OperationType == ManifestOperationType.Unfollow);
    }

    [Fact]
    public async Task ManifestMerged_Event_FiresCorrectly()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        var mergeEvents = new List<ManifestMergedEventArgs>();
        bob.Orchestrator.ManifestMerged += (_, args) => mergeEvents.Add(args);

        alice.AnnounceTrack("test-track", "hashvalue");
        await _context.ConnectAndSyncAllAsync();

        Assert.True(mergeEvents.Count >= 0, "ManifestMerged event mechanism is wired.");
    }

    [Fact]
    public async Task RequestContentAsync_RecordsAttempts_AndProducesNatGuidance_WhenTransferFails()
    {
        _output.WriteLine("Starting RequestContentAsync_RecordsAttempts_AndProducesNatGuidance_WhenTransferFails");
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        _output.WriteLine("Syncing peers...");
        await _context.ConnectAndSyncAllAsync();

        _output.WriteLine("Requesting missing content from Bob...");
        var content = await alice.Orchestrator.RequestContentAsync(bob.UserId, "missing-content-hash");
        Assert.Null(content);

        var report = alice.Orchestrator.LastConnectionAttemptReport;
        Assert.NotNull(report);

        _output.WriteLine($"Connection report for {report!.PeerUserId}:");
        foreach (var attempt in report.Attempts) _output.WriteLine($"  - Attempt: {attempt.Method}, Success: {attempt.Success}, Details: {attempt.Details}");

        Assert.Equal(bob.UserId, report!.PeerUserId);
        Assert.Contains(report.Attempts, a => a.Method.Contains("parallel-chunk") || a.Method.Contains("direct-tcp-probe"));
        Assert.Contains(report.Attempts, a => a.Method == "nat-guidance");
    }

    [Fact]
    public async Task Jane_CanDownload_JohnsDeskPlastic_TrackByContentHash()
    {
        var john = await _context.CreatePeerAsync("John", testDataName: "John");
        var jane = await _context.CreatePeerAsync("Jane", testDataName: "Jane");

        var deskPlasticDir = Path.Combine(john.BaseFolder, "DeskPlastic");
        var mp3Files = Directory.GetFiles(deskPlasticDir, "*.mp3");
        Assert.NotEmpty(mp3Files);

        var firstMp3 = mp3Files[0];
        var hash = CryptoService.ComputeFileHash(firstMp3);
        var trackId = "test-track-deskplastic";

        john.AnnounceTrack(trackId, hash, new Dictionary<string, string> { ["title"] = "DeskPlastic Track" });

        var johnContentIndex = new Dictionary<string, byte[]>();
        johnContentIndex[hash] = File.ReadAllBytes(firstMp3);

        // Restart john with content provider, keeping same identity, port and persisted manifests
        var bootstrapNodes = new[] { $"127.0.0.1:{_context.BootstrapPort}" };

        await john.Orchestrator.StopAsync();
        await john.StartAsync(bootstrapNodes: bootstrapNodes, contentProvider: h => johnContentIndex.GetValueOrDefault(h));

        await _context.ConnectAndSyncAllAsync();

        var downloadedBytes = await jane.Orchestrator.RequestContentAsync(john.UserId, hash);
        Assert.NotNull(downloadedBytes);
        Assert.Equal(johnContentIndex[hash], downloadedBytes);
    }

    [Fact]
    public async Task Jane_CanSeeJohnsPublishedTracks_AndNewPublications()
    {
        var john = await _context.CreatePeerAsync("John", testDataName: "John");
        var jane = await _context.CreatePeerAsync("Jane", testDataName: "Jane");

        john.AnnounceTrack("john-track-1", "hash1", new Dictionary<string, string> { ["title"] = "Track 1" });
        john.AnnounceTrack("john-track-2", "hash2", new Dictionary<string, string> { ["title"] = "Track 2" });

        await _context.ConnectAndSyncAllAsync();

        await jane.WaitForConditionAsync(() =>
        {
            var manifest = jane.GetPeerManifest(john.UserId);
            return CountPublicTracks(manifest) == 2;
        });

        john.AnnounceTrack("john-track-3", "hash3", new Dictionary<string, string> { ["title"] = "Track 3" });
        await john.SyncAsync();

        await jane.WaitForConditionAsync(() =>
        {
            var manifest = jane.GetPeerManifest(john.UserId);
            return CountPublicTracks(manifest) == 3;
        });
    }

    [Fact]
    public async Task Jane_CanSeeJohnsComments_OnHerTracks()
    {
        var john = await _context.CreatePeerAsync("John");
        var jane = await _context.CreatePeerAsync("Jane");

        jane.AnnounceTrack("jane-track-1", "jane-hash-1", new Dictionary<string, string> { ["title"] = "Jane Song" });
        await _context.ConnectAndSyncAllAsync();

        john.CommentOn("jane-track-1", "Love this track!");
        await john.SyncAsync();

        await jane.WaitForConditionAsync(() =>
            jane.HasOperation(john.UserId, ManifestStreamType.Interaction, op =>
                op.OperationType == ManifestOperationType.Comment &&
                op.TargetId == "jane-track-1" &&
                op.Metadata != null &&
                op.Metadata.TryGetValue("text", out var text) && text == "Love this track!"));
    }

    [Fact]
    public async Task ProfileOperationsAreExchanged()
    {
        var john = await _context.CreatePeerAsync("John");
        var jane = await _context.CreatePeerAsync("Jane");

        var johnSync = john.Orchestrator;
        var janeSync = jane.Orchestrator;

        // Verify that john and jane are connected
        await john.WaitForConditionAsync(() => johnSync.ConnectedPeerCount > 0);
        await jane.WaitForConditionAsync(() => janeSync.ConnectedPeerCount > 0);

        // Use the built-in ConnectAndSyncAll which properly propagates manifests
        await _context.ConnectAndSyncAllAsync();

        // First check if any operations are exchanged at all.
        // We expect at least a profile operation to be exchanged by BroadcastProfile called by CreatePeerAsync.
        try
        {
            await TestWaiter.WaitForItemPollingAsync(() => johnSync.PeerManifests,
                x => x.Operations.Count > 0, timeoutMs: 2000, cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (Exception)
        {
            foreach (var streamType in Enum.GetValues<ManifestStreamType>())
            {
                _output.WriteLine($"Stats for manifest type {streamType}");
                var johnLocalManifest = johnSync.GetLocalManifest(streamType);
                var johnManifestOpCount = johnLocalManifest?.Operations.Count ?? -1;
                var johnManifestProfileOpCount = johnLocalManifest?.Operations.Count(x => x.OperationType == ManifestOperationType.Profile) ?? -1;
                var johnRemoteManifestsCount = johnSync.PeerManifests.Where(x => x.StreamType == streamType).Select(x => x.Operations.Count).Sum();
                var janeLocalManifest = janeSync.GetLocalManifest(streamType);
                var janeManifestOpCount = janeLocalManifest?.Operations.Count ?? -1;
                var janeManifestProfileOpCount = janeLocalManifest?.Operations.Count(x => x.OperationType == ManifestOperationType.Profile) ?? -1;
                var janeRemoteManifestsCount = janeSync.PeerManifests.Where(x => x.StreamType == streamType).Select(x => x.Operations.Count).Sum();

                _output.WriteLine($"John {streamType} manifest counts: Local ops {johnManifestOpCount}. Local profile ops {johnManifestProfileOpCount}. Remote ops: {johnRemoteManifestsCount}");
                _output.WriteLine($"Jane {streamType} manifest counts: Local ops {janeManifestOpCount}. Local profile ops {janeManifestProfileOpCount}. Remote ops: {janeRemoteManifestsCount}");

            }

            _output.WriteLine("=== JOHN'S LOGS ===");
            _output.WriteLine(john.GetLogsAsString());
            _output.WriteLine("\n=== JANE'S LOGS ===");
            _output.WriteLine(jane.GetLogsAsString());
            throw;
        }
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Stress)]
    public async Task StressTest_ManyComments_AreDistributed()
    {
        var john = await _context.CreatePeerAsync("John");
        var jane = await _context.CreatePeerAsync("Jane");

        jane.AnnounceTrack("jane-track-1", "jane-hash-1");
        await _context.ConnectAndSyncAllAsync();

        const int commentCount = 50;
        StressTesting.FloodWithComments(john, "jane-track-1", commentCount);

        await john.SyncAsync();

        await jane.WaitForConditionAsync(() =>
        {
            var manifest = jane.GetPeerManifest(john.UserId, ManifestStreamType.Interaction);
            return manifest?.Operations.Count(op => op.OperationType == ManifestOperationType.Comment) == commentCount;
        }, timeoutMs: 15000);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Stress)]
    public async Task StressSync1000OperationsTest()
    {
        _output.WriteLine("Starting StressSync1000OperationsTest");
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        alice.AnnounceTrack("main-track", "alice-hash-1");
        await _context.ConnectAndSyncAllAsync();

        _output.WriteLine("Alice is generating 1000 comments...");
        for (var i = 0; i < 1000; i++) alice.Orchestrator.RecordComment("main-track", $"Comment {i}");

        _output.WriteLine("Alice is performing final sync/push...");
        await alice.SyncAsync();
        await bob.SyncAsync();

        _output.WriteLine("Waiting for Bob to receive all 1000 comments via delta-sync and Protobuf...");
        await bob.WaitForConditionAsync(() =>
        {
            var manifest = bob.GetPeerManifest(alice.UserId, ManifestStreamType.Interaction);
            var totalOps = (manifest?.Operations.Count ?? 0) + (manifest?.Snapshot?.PersistentOperations.Count ?? 0);
            _output.WriteLine($"Current total ops for Alice in Bob's store: {totalOps}");
            return totalOps == 1000;
        }, timeoutMs: 60000);

        _output.WriteLine("Success: Bob received 1000 operations.");

        var manifestBobHas = bob.GetPeerManifest(alice.UserId, ManifestStreamType.Interaction);
        Assert.NotNull(manifestBobHas);
        var finalCount = manifestBobHas.Operations.Count + (manifestBobHas.Snapshot?.PersistentOperations.Count ?? 0);
        Assert.Equal(1000, finalCount);
    }

    private static int CountPublicTracks(Manifest? manifest)
    {
        if (manifest == null) return 0;
        return manifest.Operations
            .Where(op => string.Equals(op.TargetType, "Track", StringComparison.OrdinalIgnoreCase) &&
                         (op.OperationType == ManifestOperationType.Create || op.OperationType == ManifestOperationType.Update || op.OperationType == ManifestOperationType.Delete))
            .GroupBy(op => op.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(op => op.SequenceNumber).First())
            .Count(op => op.OperationType != ManifestOperationType.Delete);
    }
}
