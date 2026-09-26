using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for merging hash-linked manifest streams: sequence bookkeeping, gaps, forks, replay and tampering,
/// and which operations survive compaction.
/// </summary>
public class ManifestMergeSequenceTests
{
    private readonly ManifestManager _manager = new();
    private static readonly (string privateKeyPem, string publicKeyPem) Keys = CryptoService.GenerateKeyPair();
    private static readonly string UserId = CryptoService.DeriveUserIdFromPublicKey(Keys.publicKeyPem);

    /// <summary>The author's stream, built with the real signing path.</summary>
    private Manifest AuthorStream(ManifestStreamType streamType = ManifestStreamType.Interaction)
    {
        var manifest = _manager.CreateManifest(UserId);
        manifest.StreamType = streamType;
        return manifest;
    }

    private ManifestOperation Append(Manifest stream, ManifestOperationType type, string targetId, Dictionary<string, string>? metadata = null)
    {
        return _manager.AppendSignedOperation(stream, type, targetId, "Track", null, metadata, Keys.privateKeyPem);
    }

    /// <summary>A page of the author's stream, as a peer would send it.</summary>
    private static Manifest Page(Manifest stream, int from, int? to = null)
    {
        return ManifestManager.BuildPage(stream, from, to);
    }

    private Manifest EmptyLocal(ManifestStreamType streamType = ManifestStreamType.Interaction)
    {
        var local = _manager.CreateManifest(UserId);
        local.StreamType = streamType;
        return local;
    }

    [Fact]
    public void Merge_KeepsEveryOperation_SoTheCopyEqualsTheAuthorsLog()
    {
        var author = AuthorStream();
        for (var i = 0; i < SecurityLimits.MaxPlaysPerUserPerTrackPerDay + 2; i++) Append(author, ManifestOperationType.Play, "track-1");
        Append(author, ManifestOperationType.Like, "track-1");

        var local = EmptyLocal();
        var added = _manager.MergeManifest(local, Page(author, 0), Keys.publicKeyPem);

        Assert.Equal(author.Operations.Count, added);
        Assert.Equal(ManifestManager.GetHead(author), ManifestManager.GetHead(local));
        // The play cap is applied when counting, not by dropping operations.
        Assert.Equal(SecurityLimits.MaxPlaysPerUserPerTrackPerDay, ManifestState.CountPlays(local, "track-1"));
    }

    [Fact]
    public void Merge_OfDeltas_AppendsOnlyNewOperations()
    {
        var author = AuthorStream();
        Append(author, ManifestOperationType.Like, "t0");
        Append(author, ManifestOperationType.Like, "t1");

        var local = EmptyLocal();
        _manager.MergeManifest(local, Page(author, 0), Keys.publicKeyPem);

        Append(author, ManifestOperationType.Like, "t2");
        Append(author, ManifestOperationType.Like, "t3");

        // The full stream again adds nothing twice; the delta adds the two new operations.
        Assert.Equal(2, _manager.MergeManifest(local, Page(author, 0), Keys.publicKeyPem));
        Assert.Equal(0, _manager.MergeManifest(local, Page(author, 2), Keys.publicKeyPem));
        Assert.Equal(3, ManifestManager.GetHeadSequenceNumber(local));
        Assert.Equal(local.Operations.Count, local.Operations.Select(o => o.SequenceNumber).Distinct().Count());
    }

    [Fact]
    public void Merge_RejectsOperationsThatWouldLeaveAGap()
    {
        var author = AuthorStream();
        for (var i = 0; i < 7; i++) Append(author, ManifestOperationType.Like, $"t{i}");

        var local = EmptyLocal();
        _manager.MergeManifest(local, Page(author, 0, 0), Keys.publicKeyPem);

        var added = _manager.MergeManifest(local, Page(author, 5), Keys.publicKeyPem);

        Assert.Equal(0, added);
        Assert.Equal(0, ManifestManager.GetHeadSequenceNumber(local));
    }

    [Fact]
    public void Merge_DetectsAFork_AndKeepsTheFirstVersion()
    {
        var author = AuthorStream();
        Append(author, ManifestOperationType.Like, "t0");
        var forkBase = ManifestManager.BuildPage(author, 0);

        var local = EmptyLocal();
        _manager.MergeManifest(local, Page(author, 0), Keys.publicKeyPem);
        Append(author, ManifestOperationType.Like, "version-a");
        _manager.MergeManifest(local, Page(author, 1), Keys.publicKeyPem);

        // The author signs a different operation #1 (e.g. restored an old backup) and a peer offers it.
        var other = EmptyLocal();
        _manager.MergeManifest(other, forkBase, Keys.publicKeyPem);
        Append(other, ManifestOperationType.Like, "version-b");

        var forks = new List<int>();
        _manager.ForkDetected += (_, _, seq) => forks.Add(seq);
        var added = _manager.MergeManifest(local, Page(other, 0), Keys.publicKeyPem);

        Assert.Equal(0, added);
        Assert.Equal([1], forks);
        Assert.Equal("version-a", local.Operations.Single(o => o.SequenceNumber == 1).TargetId);
    }

    [Fact]
    public void Merge_RejectsADeltaThatDoesNotChainToTheLocalHead()
    {
        var author = AuthorStream();
        Append(author, ManifestOperationType.Like, "t0");
        var local = EmptyLocal();
        _manager.MergeManifest(local, Page(author, 0), Keys.publicKeyPem);

        // A validly signed operation #1 whose PrevHash does not point at our operation #0.
        var stray = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(), OperationType = ManifestOperationType.Like, TargetId = "stray", TargetType = "Track",
            SequenceNumber = 1, PrevHash = "not-the-head", Signature = string.Empty
        };
        stray.Signature = CryptoService.SignData(ManifestManager.BuildSignablePayload(stray), Keys.privateKeyPem);

        var added = _manager.MergeManifest(local, new Manifest { UserId = UserId, StreamType = ManifestStreamType.Interaction, Operations = [stray] }, Keys.publicKeyPem);

        Assert.Equal(0, added);
        Assert.Equal(0, ManifestManager.GetHeadSequenceNumber(local));
    }

    [Fact]
    public void Verify_RejectsAnOperationReplayedIntoAnotherStream()
    {
        // The author's first Content operation (a track release) replayed as the first operation of its Social stream.
        var content = AuthorStream(ManifestStreamType.Content);
        _manager.AppendSignedOperation(content, ManifestOperationType.Create, "track-1", "Track", "hash", null, Keys.privateKeyPem);

        var replayed = new Manifest { UserId = UserId, StreamType = ManifestStreamType.Social, Operations = content.Operations.ToList() };

        Assert.True(_manager.VerifyManifest(content, Keys.publicKeyPem));
        Assert.False(_manager.VerifyManifest(replayed, Keys.publicKeyPem));
    }

    [Fact]
    public void Verify_RejectsTamperedMetadata()
    {
        // Metadata (track title, artist, shader script, ...) is signed, so a peer that forwards a release cannot alter it.
        var content = AuthorStream(ManifestStreamType.Content);
        _manager.AppendSignedOperation(content, ManifestOperationType.Create, "track-1", "Track", "hash",
            new Dictionary<string, string> { ["title"] = "Original" }, Keys.privateKeyPem);

        var page = Page(content, 0);
        var forged = new Manifest
        {
            UserId = UserId, StreamType = ManifestStreamType.Content,
            Operations = [new ManifestOperation
            {
                OperationId = page.Operations[0].OperationId, OperationType = page.Operations[0].OperationType,
                TargetId = page.Operations[0].TargetId, TargetType = page.Operations[0].TargetType,
                ContentHash = page.Operations[0].ContentHash, SequenceNumber = 0, Timestamp = page.Operations[0].Timestamp,
                PrevHash = page.Operations[0].PrevHash, Signature = page.Operations[0].Signature,
                Metadata = new Dictionary<string, string> { ["title"] = "Spoofed" }
            }]
        };

        Assert.False(_manager.VerifyManifest(forged, Keys.publicKeyPem));
    }

    [Fact]
    public void BuildPage_SplitsLargeStreams_AndPagesMergeInOrder()
    {
        var author = AuthorStream();
        var text = new string('x', 1500);
        for (var i = 0; i < 30; i++) Append(author, ManifestOperationType.Comment, $"t{i}", new Dictionary<string, string> { ["text"] = text });

        var local = EmptyLocal();
        var pages = 0;
        Manifest page;
        do
        {
            page = ManifestManager.BuildPage(author, ManifestManager.GetHeadSequenceNumber(local) + 1, maxPageBytes: 8 * 1024);
            _manager.MergeManifest(local, page, Keys.publicKeyPem);
            pages++;
        } while (page.HasMore && pages < 100);

        Assert.True(pages > 1);
        Assert.Equal(ManifestManager.GetHead(author), ManifestManager.GetHead(local));
    }

    [Fact]
    public void BuildPage_IncludesTheAuthorKey_OnlyFromTheStart()
    {
        var author = AuthorStream();
        author.AuthorPublicKey = Keys.publicKeyPem;
        Append(author, ManifestOperationType.Like, "t0");
        Append(author, ManifestOperationType.Like, "t1");

        Assert.Equal(Keys.publicKeyPem, ManifestManager.BuildPage(author, 0).AuthorPublicKey);
        Assert.Null(ManifestManager.BuildPage(author, 1).AuthorPublicKey);
    }

    [Fact]
    public void GetHeadSequenceNumber_UsesHighestSequenceNotCount()
    {
        Assert.Equal(-1, ManifestManager.GetHeadSequenceNumber(null));

        var manifest = new Manifest
        {
            UserId = UserId,
            Snapshot = new ManifestSnapshot { LastSequenceNumber = 9, Signature = string.Empty },
            Operations = [new ManifestOperation { OperationId = "a", OperationType = ManifestOperationType.Like, TargetId = "a", TargetType = "Track", SequenceNumber = 12, Signature = string.Empty }]
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
        var manifest = AuthorStream(ManifestStreamType.Social);
        Append(manifest, type, "group-1");
        Append(manifest, ManifestOperationType.Follow, "someone");

        var snapshot = _manager.CreateSnapshot(manifest, upToSequenceNumber: 1, Keys.privateKeyPem);

        Assert.Contains(snapshot.PersistentOperations, o => o.OperationType == type);
    }

    [Fact]
    public void CreateSnapshot_DropsTheOldestComments_BeyondTheRetentionCap()
    {
        var manifest = AuthorStream();
        var total = SecurityLimits.MaxSnapshotRetainedOperations + 5;
        for (var i = 0; i < total; i++)
            Append(manifest, ManifestOperationType.Comment, "track-1", new Dictionary<string, string> { ["text"] = $"c{i}" });

        var snapshot = _manager.CreateSnapshot(manifest, total - 1, Keys.privateKeyPem);
        manifest.Snapshot = snapshot;
        manifest.Operations.Clear();

        Assert.Equal(SecurityLimits.MaxSnapshotRetainedOperations, snapshot.PersistentOperations.Count);
        Assert.Equal("c5", snapshot.PersistentOperations[0].Metadata["text"]);
        Assert.True(_manager.VerifyManifest(manifest, Keys.publicKeyPem));
    }

    [Fact]
    public void Merge_OfANewerSnapshot_KeepsLocalOperationsThatContinueIt()
    {
        var author = AuthorStream();
        for (var i = 0; i < 10; i++) Append(author, ManifestOperationType.Like, $"t{i}");

        var local = EmptyLocal();
        _manager.MergeManifest(local, Page(author, 0), Keys.publicKeyPem);

        // The author compacts up to #5; a peer sends only the snapshot and #6.
        _manager.Compact(author, Keys.privateKeyPem, threshold: 5, keepRecent: 4);
        _manager.MergeManifest(local, Page(author, 0, 6), Keys.publicKeyPem);

        Assert.NotNull(local.Snapshot);
        Assert.Equal(ManifestManager.GetHead(author), ManifestManager.GetHead(local));
        Assert.True(_manager.VerifyManifest(local, Keys.publicKeyPem));
    }

    [Fact]
    public void EnsureSignedChain_ResignsAManifestWrittenBeforeChaining()
    {
        var legacy = AuthorStream();
        Append(legacy, ManifestOperationType.Like, "t0");
        Append(legacy, ManifestOperationType.Like, "t1");
        foreach (var op in legacy.Operations) op.PrevHash = string.Empty; // as stored by older versions
        Assert.False(_manager.VerifyManifest(legacy, Keys.publicKeyPem));

        Assert.True(_manager.EnsureSignedChain(legacy, Keys.privateKeyPem, Keys.publicKeyPem));

        Assert.True(_manager.VerifyManifest(legacy, Keys.publicKeyPem));
        Assert.False(_manager.EnsureSignedChain(legacy, Keys.privateKeyPem, Keys.publicKeyPem));
    }
}
