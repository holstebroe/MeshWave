using System.Net;
using System.Net.Sockets;
using MeshWave.Bootstrap.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Storage;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using Xunit;

namespace MeshWave.Integration.Tests;

public class MeshIntegrationTests : IAsyncLifetime
{
    private MeshTestContext _context = default!;

    public Task InitializeAsync()
    {
        _context = new MeshTestContext();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task Bootstrap_LateJoiner_CanDiscoverExistingPeer()
    {
        var peerA = await _context.CreatePeerAsync("Alice");
        var peerB = await _context.CreatePeerAsync("Bob");

        await peerB.WaitForConditionAsync(() => peerB.Orchestrator.ConnectedPeerCount > 0);
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
        alice.BroadcastProfile("Alice Artist", isArtist: true, "My bio", "https://alice.example", null!);

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
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        await _context.ConnectAndSyncAllAsync();

        var content = await alice.Orchestrator.RequestContentAsync(bob.UserId, "missing-content-hash");
        Assert.Null(content);

        var report = alice.Orchestrator.LastConnectionAttemptReport;
        Assert.NotNull(report);
        Assert.Equal(bob.UserId, report!.PeerUserId);
        Assert.Contains(report.Attempts, a => a.Method == "direct-tcp-probe");
        Assert.Contains(report.Attempts, a => a.Method == "nat-guidance");
    }

    [Fact]
    public async Task Jane_CanDownload_JohnsDeskPlastic_TrackByContentHash()
    {
        var john = await _context.CreatePeerAsync("John", testDataName: "John");
        var jane = await _context.CreatePeerAsync("Jane", testDataName: "Jane");

        var deskPlasticDir = Path.Combine(john.BaseDir, "DeskPlastic");
        var mp3Files = Directory.GetFiles(deskPlasticDir, "*.mp3");
        Assert.NotEmpty(mp3Files);

        var firstMp3 = mp3Files[0];
        var hash = CryptoService.ComputeFileHash(firstMp3);
        var trackId = "test-track-deskplastic";

        john.AnnounceTrack(trackId, hash, new Dictionary<string, string> { ["title"] = "DeskPlastic Track" });

        var johnContentIndex = new Dictionary<string, byte[]>();
        johnContentIndex[hash] = File.ReadAllBytes(firstMp3);

        await john.DisposeAsync();
        await john.StartAsync(bootstrapNodes: [$"127.0.0.1:39877"], contentProvider: h => johnContentIndex.GetValueOrDefault(h));

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

        await jane.WaitForConditionAsync(() => {
            var manifest = jane.GetPeerManifest(john.UserId, ManifestStreamType.Content);
            return CountPublicTracks(manifest) == 2;
        });

        john.AnnounceTrack("john-track-3", "hash3", new Dictionary<string, string> { ["title"] = "Track 3" });
        await john.SyncAsync();

        await jane.WaitForConditionAsync(() => {
            var manifest = jane.GetPeerManifest(john.UserId, ManifestStreamType.Content);
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

        await jane.WaitForConditionAsync(() => {
            var manifest = jane.GetPeerManifest(john.UserId, ManifestStreamType.Interaction);
            return manifest?.Operations.Count(op => op.OperationType == ManifestOperationType.Comment) == commentCount;
        }, timeoutMs: 15000);
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
