using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for play-count manifest operations:
/// - RecordPlay session rate cap (one per track per session in SyncOrchestrator)
/// - MergeManifest daily play cap enforcement (MaxPlaysPerUserPerTrackPerDay)
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
        Manifest manifest, string trackId, DateTime utcTimestamp, string privateKeyPem)
    {
        var seq = manifest.Operations.Count;
        var op = new ManifestOperation
        {
            OperationId  = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.Play,
            TargetId     = trackId,
            TargetType   = "Track",
            Signature    = string.Empty,
            Timestamp    = utcTimestamp,
            SequenceNumber = seq,
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
        var orchestrator = new SyncOrchestrator();
        var result = orchestrator.RecordPlay("track-1", "Title", "Artist");
        Assert.False(result);
    }

    [Fact]
    public void RecordPlay_ReturnsFalse_ForBlankTrackId()
    {
        var orchestrator = new SyncOrchestrator();
        var result = orchestrator.RecordPlay("   ", "Title", "Artist");
        Assert.False(result);
    }

    // ─── MergeManifest daily play cap ────────────────────────────────────────

    [Fact]
    public void MergeManifest_AcceptsPlays_UpToDailyCap()
    {
        var (pub, priv) = GenerateKeyPair();
        var local  = _manager.CreateManifest("user-merge-1");
        var remote = _manager.CreateManifest("user-merge-1");

        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay; i++)
            AppendPlayAt(remote, "track-a", DateTime.UtcNow, priv);

        var added = _manager.MergeManifest(local, remote, pub);

        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, added);
    }

    [Fact]
    public void MergeManifest_DropsExcessPlays_BeyondDailyCap()
    {
        var (pub, priv) = GenerateKeyPair();
        var local  = _manager.CreateManifest("user-merge-2");
        var remote = _manager.CreateManifest("user-merge-2");

        var overCount = SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 5;
        for (var i = 0; i < overCount; i++)
            AppendPlayAt(remote, "track-b", DateTime.UtcNow, priv);

        var added = _manager.MergeManifest(local, remote, pub);

        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, added);
    }

    [Fact]
    public void MergeManifest_CountsExistingLocalPlaysTowardCap()
    {
        var (pub, priv) = GenerateKeyPair();

        // Local already has (cap-1) plays — built properly via AppendPlayAt
        var local = _manager.CreateManifest("user-merge-3");
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay - 1; i++)
            AppendPlayAt(local, "track-c", DateTime.UtcNow, priv);

        // Remote must be a valid full manifest (or at least continuous from 0 if no snapshot)
        var remote = _manager.CreateManifest("user-merge-3");
        // Add same initial plays
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay - 1; i++)
            AppendPlayAt(remote, "track-c", DateTime.UtcNow, priv);
        // Add 3 new ones
        for (var i = 0; i < 3; i++)
            AppendPlayAt(remote, "track-c", DateTime.UtcNow, priv);

        var added = _manager.MergeManifest(local, remote, pub);

        // Only 1 play should be accepted (fills the cap)
        Assert.Equal(1, added);
    }

    [Fact]
    public void MergeManifest_CapIsPerTrack_DifferentTracksCountSeparately()
    {
        var (pub, priv) = GenerateKeyPair();
        var local  = _manager.CreateManifest("user-merge-4");
        var remote = _manager.CreateManifest("user-merge-4");

        // Add cap plays for track-x then cap plays for track-y in one sequential manifest
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay; i++)
            AppendPlayAt(remote, "track-x", DateTime.UtcNow, priv);
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay; i++)
            AppendPlayAt(remote, "track-y", DateTime.UtcNow, priv);

        var added = _manager.MergeManifest(local, remote, pub);

        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay * 2, added);
    }

    [Fact]
    public void MergeManifest_CapIsPerDay_DifferentDaysCountSeparately()
    {
        var (pub, priv) = GenerateKeyPair();
        var local  = _manager.CreateManifest("user-merge-5");
        var remote = _manager.CreateManifest("user-merge-5");

        var today     = DateTime.UtcNow.Date.AddHours(12);
        var yesterday = today.AddDays(-1);

        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay; i++)
            AppendPlayAt(remote, "track-d", today, priv);
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay; i++)
            AppendPlayAt(remote, "track-d", yesterday, priv);

        var added = _manager.MergeManifest(local, remote, pub);

        // Both days should each get their full quota
        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay * 2, added);
    }

    [Fact]
    public void MergeManifest_NonPlayOperations_AreNotAffectedByCap()
    {
        var (pub, priv) = GenerateKeyPair();
        var local  = _manager.CreateManifest("user-merge-6");
        var remote = _manager.CreateManifest("user-merge-6");

        var overCount = SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 2;
        for (var i = 0; i < overCount; i++)
            AppendPlayAt(remote, "track-e", DateTime.UtcNow, priv);

        // Append a Create op — not subject to play cap
        _manager.AppendSignedOperation(
            remote, ManifestOperationType.Create, "track-e", "Track",
            "hash-abc", null, priv);

        var added = _manager.MergeManifest(local, remote, pub);

        // Capped plays + the 1 Create op
        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1, added);
    }
}
