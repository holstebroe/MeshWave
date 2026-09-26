using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core;
using MeshWave.TestUtilities;
using Xunit;
using System.IO;
using MeshWave.LibraryManager;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for play-count manifest operations:
/// - RecordPlay session rate cap (one per track per session in SyncOrchestrator)
/// - Daily play cap (MaxPlaysPerUserPerTrackPerDay), applied when plays are counted (ManifestState)
/// </summary>
public class PlayCountTests
{
    private readonly ManifestManager _manager = new();

    // ─── helpers ────────────────────────────────────────────────────────────

    private static (string publicKeyPem, string privateKeyPem) GenerateKeyPair()
    {
        var (priv, pub) = CryptoService.GenerateKeyPair();
        return (pub, priv);
    }

    /// <summary>
    /// Appends a Play operation with a specific timestamp (for date-boundary tests).
    /// SequenceNumber is set BEFORE signing so verification matches.
    /// </summary>
    private static void AppendPlayAt(
        Manifest manifest, string trackId, DateTime utcTimestamp, string privateKeyPem, string? contentHash = null)
    {
        var seq = ManifestManager.GetHeadSequenceNumber(manifest) + 1;
        var op = new ManifestOperation
        {
            OperationId  = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.Play,
            TargetId     = trackId,
            TargetType   = "Track",
            ContentHash  = contentHash,
            Signature    = string.Empty,
            Timestamp    = utcTimestamp,
            SequenceNumber = seq,
            PrevHash     = ManifestManager.GetHeadHash(manifest),
            Metadata     = new Dictionary<string, string> { ["title"] = "Test Track" }
        };
        // Use ManifestManager to build the signable payload to ensure consistency.
        var payload = ManifestManager.BuildSignablePayload(op);
        op.Signature = CryptoService.SignData(payload, privateKeyPem);
        manifest.Operations.Add(op);
        manifest.Version++;
        manifest.LastUpdated = DateTime.UtcNow;
    }

    // ─── SyncOrchestrator.RecordPlay session cap ─────────────────────────────

    [Fact]
    public void RecordPlay_ReturnsFalse_WhenNotStarted()
    {
        var orchestrator = CreateDummyOrchestrator();
        var result = orchestrator.RecordPlay("track-1", "Title", "Artist");
        Assert.False(result);
    }

    [Fact]
    public void RecordPlay_ReturnsFalse_ForBlankTrackId()
    {
        var orchestrator = CreateDummyOrchestrator();
        var result = orchestrator.RecordPlay("   ", "Title", "Artist");
        Assert.False(result);
    }

    private SyncOrchestrator CreateDummyOrchestrator()
    {
        var env = new DummyEnvironment(Path.GetTempPath());
        return new SyncOrchestrator(
            new PeerRouter(new PeerDiscovery(), new ManifestExchangeClient(timeoutMs: 100)),
            new ManifestExchangeClient(timeoutMs: 100),
            new ManifestManager(),
            new PeerManifestStore(env, Path.GetTempPath()),
            new ContentExchange(),
            new NatTraversalService(logger: null),
            new CatalogueService(MeshWave.Common.Core.Processors.CatalogueProcessorDefaults.GetDefaultProcessors()),
            env
        );
    }

    // ─── Daily play cap (applied when counting) ─────────────────────────────

    /// <summary>Merges the author's stream into an empty copy, as a peer would, and returns the copy.</summary>
    private Manifest Replicate(Manifest remote, string publicKeyPem)
    {
        var local = _manager.CreateManifest(remote.UserId);
        local.StreamType = remote.StreamType;
        _manager.MergeManifest(local, remote, publicKeyPem);
        return local;
    }

    [Fact]
    public void CountPlays_CountsPlays_UpToDailyCap()
    {
        var (pub, priv) = GenerateKeyPair();
        var remote = _manager.CreateManifest("user-merge-1");

        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay; i++)
            AppendPlayAt(remote, "track-a", DateTime.UtcNow, priv);

        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, ManifestState.CountPlays(Replicate(remote, pub), "track-a"));
    }

    [Fact]
    public void CountPlays_IgnoresExcessPlays_BeyondDailyCap_ButMergeKeepsThem()
    {
        var (pub, priv) = GenerateKeyPair();
        var remote = _manager.CreateManifest("user-merge-2");

        var overCount = SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 5;
        for (var i = 0; i < overCount; i++)
            AppendPlayAt(remote, "track-b", DateTime.UtcNow, priv);

        var local = Replicate(remote, pub);

        Assert.Equal(overCount, local.Operations.Count);
        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, ManifestState.CountPlays(local, "track-b"));
    }

    [Fact]
    public void CountPlays_CapIsPerTrack_DifferentTracksCountSeparately()
    {
        var (pub, priv) = GenerateKeyPair();
        var remote = _manager.CreateManifest("user-merge-4");

        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1; i++)
            AppendPlayAt(remote, "track-x", DateTime.UtcNow, priv);
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1; i++)
            AppendPlayAt(remote, "track-y", DateTime.UtcNow, priv);

        var local = Replicate(remote, pub);

        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, ManifestState.CountPlays(local, "track-x"));
        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, ManifestState.CountPlays(local, "track-y"));
    }

    [Fact]
    public void CountPlays_CapIsPerDay_DifferentDaysCountSeparately()
    {
        var (pub, priv) = GenerateKeyPair();
        var remote = _manager.CreateManifest("user-merge-5");

        var today     = DateTime.UtcNow.Date.AddHours(12);
        var yesterday = today.AddDays(-1);

        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1; i++)
            AppendPlayAt(remote, "track-d", today, priv);
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1; i++)
            AppendPlayAt(remote, "track-d", yesterday, priv);

        // Both days each get their full quota
        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay * 2, ManifestState.CountPlays(Replicate(remote, pub), "track-d"));
    }

    [Fact]
    public void CountPlays_AddsThePlayCountSquashedIntoTheSnapshot()
    {
        var (pub, priv) = GenerateKeyPair();
        var remote = _manager.CreateManifest("user-merge-6");
        var yesterday = DateTime.UtcNow.Date.AddHours(-12);

        for (var i = 0; i < 2; i++)
            AppendPlayAt(remote, "track-e", yesterday, priv);
        remote.Snapshot = _manager.CreateSnapshot(remote, ManifestManager.GetHeadSequenceNumber(remote), priv);
        remote.Operations.Clear();
        AppendPlayAt(remote, "track-e", DateTime.UtcNow, priv);

        Assert.Equal(3, ManifestState.CountPlays(Replicate(remote, pub), "track-e"));
    }

    [Fact]
    public void CreateSnapshot_PlayCount_TracksVersionedHashes()
    {
        // Arrange
        var manager = new ManifestManager();
        var (pub, priv) = GenerateKeyPair();
        var manifest = manager.CreateManifest("user1");

        AppendPlayAt(manifest, "track-1", DateTime.UtcNow.AddMinutes(-5), priv, "hash1");
        AppendPlayAt(manifest, "track-1", DateTime.UtcNow.AddMinutes(-4), priv, "hash2");
        AppendPlayAt(manifest, "track-1", DateTime.UtcNow.AddMinutes(-3), priv, "hash2");

        // Act
        var snapshot = manager.CreateSnapshot(manifest, manifest.Operations.Count - 1, priv);

        // Assert
        Assert.Equal(3, snapshot.PlayCounts["track-1"]);
        Assert.Equal(1, snapshot.PlayCounts["track-1:hash1"]);
        Assert.Equal(2, snapshot.PlayCounts["track-1:hash2"]);
    }
}
