using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for sequence-number bookkeeping during merge when operations are discarded or missing,
/// and for which operations survive compaction.
/// </summary>
public class ManifestMergeSequenceTests
{
    private readonly ManifestManager _manager = new();
    private static readonly (string privateKeyPem, string publicKeyPem) Keys = CryptoService.GenerateKeyPair();
    private static readonly string UserId = CryptoService.DeriveUserIdFromPublicKey(Keys.publicKeyPem);

    private static ManifestOperation SignedOp(int seq, ManifestOperationType type, string targetId, DateTime? timestamp = null)
    {
        var op = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = type,
            TargetId = targetId,
            TargetType = "Track",
            Signature = string.Empty,
            Timestamp = timestamp ?? DateTime.UtcNow,
            SequenceNumber = seq,
            Metadata = []
        };
        op.Signature = CryptoService.SignData(ManifestManager.BuildSignablePayload(op), Keys.privateKeyPem);
        return op;
    }

    private static Manifest RemoteManifest(params ManifestOperation[] ops)
    {
        return new Manifest { UserId = UserId, StreamType = ManifestStreamType.Interaction, Operations = ops.ToList() };
    }

    private Manifest EmptyLocal()
    {
        var local = _manager.CreateManifest(UserId);
        local.StreamType = ManifestStreamType.Interaction;
        return local;
    }

    [Fact]
    public void Merge_AfterDiscardedOperation_DoesNotDuplicateLaterOperations()
    {
        var day = DateTime.UtcNow.Date.AddHours(12);
        var plays = Enumerable.Range(0, SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1)
            .Select(i => SignedOp(i, ManifestOperationType.Play, "track-1", day))
            .ToList();
        var likeSeq = plays.Count;
        var like = SignedOp(likeSeq, ManifestOperationType.Like, "track-1");
        var laterLike = SignedOp(likeSeq + 1, ManifestOperationType.Like, "track-2");

        var local = EmptyLocal();
        _manager.MergeManifest(local, RemoteManifest([.. plays, like]), Keys.publicKeyPem);

        // The same full manifest arrives again with one more op (e.g. the next push).
        var added = _manager.MergeManifest(local, RemoteManifest([.. plays, like, laterLike]), Keys.publicKeyPem);

        Assert.Equal(1, added);
        var seqs = local.Operations.Select(o => o.SequenceNumber).ToList();
        Assert.Equal(seqs.Distinct().Count(), seqs.Count);
        Assert.Equal(likeSeq + 1, ManifestManager.GetHeadSequenceNumber(local));
    }

    [Fact]
    public void Merge_AfterDiscardedTailOperation_AcceptsTheNextOperation()
    {
        var day = DateTime.UtcNow.Date.AddHours(12);
        var plays = Enumerable.Range(0, SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 1)
            .Select(i => SignedOp(i, ManifestOperationType.Play, "track-1", day))
            .ToList();

        var local = EmptyLocal();
        _manager.MergeManifest(local, RemoteManifest([.. plays]), Keys.publicKeyPem);
        var headAfterFirstMerge = ManifestManager.GetHeadSequenceNumber(local);

        // A delta fetch starts at head + 1, so it re-sends the discarded tail op followed by the new one.
        var next = SignedOp(plays.Count, ManifestOperationType.Like, "track-1");
        var added = _manager.MergeManifest(local, RemoteManifest(plays[^1], next), Keys.publicKeyPem);

        Assert.Equal(plays.Count - 2, headAfterFirstMerge);
        Assert.Equal(1, added);
        Assert.Contains(local.Operations, o => o.SequenceNumber == plays.Count);
    }

    [Fact]
    public void Merge_RejectsOperationsThatWouldLeaveAGap()
    {
        var local = EmptyLocal();
        _manager.MergeManifest(local, RemoteManifest(SignedOp(0, ManifestOperationType.Like, "t0")), Keys.publicKeyPem);

        var added = _manager.MergeManifest(local, RemoteManifest(
            SignedOp(5, ManifestOperationType.Like, "t5"),
            SignedOp(6, ManifestOperationType.Like, "t6")), Keys.publicKeyPem);

        Assert.Equal(0, added);
        Assert.Equal(0, ManifestManager.GetHeadSequenceNumber(local));
    }

    [Fact]
    public void GetHeadSequenceNumber_UsesHighestSequenceNotCount()
    {
        Assert.Equal(-1, ManifestManager.GetHeadSequenceNumber(null));

        var manifest = new Manifest
        {
            UserId = UserId,
            Snapshot = new ManifestSnapshot { LastSequenceNumber = 9, Signature = string.Empty },
            Operations = [SignedOp(10, ManifestOperationType.Like, "a"), SignedOp(12, ManifestOperationType.Like, "b")]
        };

        Assert.Equal(12, ManifestManager.GetHeadSequenceNumber(manifest));
    }

    [Theory]
    [InlineData(ManifestOperationType.PostMessage)]
    [InlineData(ManifestOperationType.CreateChannel)]
    [InlineData(ManifestOperationType.FoundGroup)]
    [InlineData(ManifestOperationType.ModerateGroup)]
    public void CreateSnapshot_KeepsGroupOperations(ManifestOperationType type)
    {
        var manifest = new Manifest { UserId = UserId, StreamType = ManifestStreamType.Social };
        manifest.Operations.Add(SignedOp(0, type, "group-1"));
        manifest.Operations.Add(SignedOp(1, ManifestOperationType.Follow, "someone"));

        var snapshot = _manager.CreateSnapshot(manifest, upToSequenceNumber: 1, Keys.privateKeyPem);

        Assert.Contains(snapshot.PersistentOperations, o => o.OperationType == type);
    }
}
