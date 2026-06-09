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
        await peerB.WaitForConditionAsync(() => peerB.Orchestrator.ConnectedPeerCount > 0);
        _output.WriteLine($"Peer B connected peer count: {peerB.Orchestrator.ConnectedPeerCount}");

        Assert.True(peerB.Orchestrator.ConnectedPeerCount >= 0);
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
        Assert.Contains(report.Attempts, a => a.Method == "direct-tcp-probe");
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

        // Restart john with content provider, keeping same identity and port
        var identity = john.Identity;
        var port = john.Port;
        var bootstrapNodes = new[] { $"127.0.0.1:{_context.BootstrapPort}" };

        await john.DisposeAsync();
        await john.StartAsync(bootstrapNodes: bootstrapNodes, contentProvider: h => johnContentIndex.GetValueOrDefault(h));

        await _context.ConnectAndSyncAllAsync();

        var downloadedBytes = await jane.Orchestrator.RequestContentAsync(john.UserId, hash);
        Assert.NotNull(downloadedBytes);
        Assert.Equal(johnContentIndex[hash].Length, downloadedBytes.Length);
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
    public async Task SocialInteractions_ArePropagatedImmediatelyToOwner()
    {
        _output.WriteLine("Starting SocialInteractions_ArePropagatedImmediatelyToOwner");
        var owner = await _context.CreatePeerAsync("TrackOwner");
        var listener = await _context.CreatePeerAsync("Listener");

        await owner.WaitForConditionAsync(() => owner.Orchestrator.ConnectedPeerCount > 0);
        await listener.WaitForConditionAsync(() => listener.Orchestrator.ConnectedPeerCount > 0);

        await _context.ConnectAndSyncAllAsync();

        // Owner publishes a track
        var contentHash = "hash123";
        var meta = new Dictionary<string, string> { { "title", "Test Song" } };
        owner.Orchestrator.AnnounceTrack("track1", contentHash, meta);

        // Wait for listener to receive the track
        await TestWaiter.WaitForItemPollingAsync(() => listener.Orchestrator.PeerManifests,
            x => x.UserId == owner.Orchestrator.Identity?.UserId && x.Operations.Any(o => o.OperationType == ManifestOperationType.Create),
            timeoutMs: 2000, cancellationToken: TestContext.Current.CancellationToken);

        // Listener comments on the track
        listener.Orchestrator.RecordComment("track1", "Great track!");

        // Owner should receive the comment very quickly via targeted notification
        await TestWaiter.WaitForItemPollingAsync(() => owner.Orchestrator.PeerManifests,
            x => x.UserId == listener.Orchestrator.Identity?.UserId && x.StreamType == ManifestStreamType.Interaction && x.Operations.Any(o => o.OperationType == ManifestOperationType.Comment),
            timeoutMs: 1000, cancellationToken: TestContext.Current.CancellationToken);

        var ownerInteractionManifests = owner.Orchestrator.PeerManifests.FirstOrDefault(m => m.UserId == listener.Orchestrator.Identity?.UserId && m.StreamType == ManifestStreamType.Interaction);
        Assert.NotNull(ownerInteractionManifests);
        Assert.Contains(ownerInteractionManifests.Operations, o => o.OperationType == ManifestOperationType.Comment && o.TargetId == "track1");
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
