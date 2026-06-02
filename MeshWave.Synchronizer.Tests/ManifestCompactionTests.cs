using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class ManifestCompactionTests
{
    private readonly ManifestManager _manager = new();

    [Fact]
    public void Compact_ReducesOperationCount_AndCreatesValidSnapshot()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        // Add many operations
        for (int i = 0; i < 20; i++)
        {
            _manager.AppendSignedOperation(manifest, ManifestOperationType.Play,
                "track-1", "Track", null, null, privateKey);
        }

        Assert.Equal(20, manifest.Operations.Count);

        // Compact: threshold 15, keep 5
        _manager.Compact(manifest, privateKey, threshold: 15, keepRecent: 5);

        Assert.Equal(5, manifest.Operations.Count);
        Assert.NotNull(manifest.Snapshot);
        Assert.Equal(14, manifest.Snapshot.LastSequenceNumber); // 0-14 squashed, 15-19 kept
        Assert.Equal(15, manifest.Snapshot.PlayCounts["track-1"]);

        // Verify manifest still passes
        Assert.True(_manager.VerifyManifest(manifest, publicKey));
    }

    [Fact]
    public void CreateSnapshot_SquashesRedundantOperations()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Follow, "user-2", "User", null, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Unfollow, "user-2", "User", null, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Follow, "user-3", "User", null, null, privateKey);

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Like, "track-1", "Track", null, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Unlike, "track-1", "Track", null, null, privateKey);

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Profile, "user-1", "User", null, new Dictionary<string, string> { ["displayName"] = "Alice" }, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Profile, "user-1", "User", null, new Dictionary<string, string> { ["displayName"] = "Alice In Wonderland" }, privateKey);

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Comment, "track-1", "Track", null, new Dictionary<string, string> { ["text"] = "Cool!" }, privateKey);

        var snapshot = _manager.CreateSnapshot(manifest, manifest.Operations.Count - 1, privateKey);

        Assert.Single(snapshot.FollowedUserIds);
        Assert.Equal("user-3", snapshot.FollowedUserIds[0]);
        Assert.Empty(snapshot.LikedTrackIds);

        var profile = snapshot.EntityStates.First(e => e.TargetType == "User");
        Assert.Equal("Alice In Wonderland", profile.Metadata["displayName"]);

        Assert.Single(snapshot.PersistentOperations);
        Assert.Equal("Cool!", snapshot.PersistentOperations[0].Metadata["text"]);
    }

    [Fact]
    public void MergeManifest_HandlesRemoteSnapshot()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        var local = _manager.CreateManifest("user-1");

        var remote = _manager.CreateManifest("user-1");
        for (int i = 0; i < 10; i++)
        {
            _manager.AppendSignedOperation(remote, ManifestOperationType.Play, "track-1", "Track", null, null, privateKey);
        }

        _manager.Compact(remote, privateKey, threshold: 5, keepRecent: 2);

        // Merge remote into local
        int added = _manager.MergeManifest(local, remote, publicKey);

        Assert.Equal(2, added);
        Assert.NotNull(local.Snapshot);
        Assert.NotNull(remote.Snapshot);
        Assert.Equal(remote.Snapshot!.LastSequenceNumber, local.Snapshot!.LastSequenceNumber);
        Assert.Equal(2, local.Operations.Count);
        Assert.True(_manager.VerifyManifest(local, publicKey));
    }

    [Fact]
    public void MergeManifest_UpdatesExistingLocalWithRemoteSnapshot()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        var local = _manager.CreateManifest("user-1");
        for (int i = 0; i < 5; i++)
        {
            _manager.AppendSignedOperation(local, ManifestOperationType.Play, "track-1", "Track", null, null, privateKey);
        }

        var remote = _manager.CreateManifest("user-1");
        for (int i = 0; i < 10; i++)
        {
            _manager.AppendSignedOperation(remote, ManifestOperationType.Play, "track-1", "Track", null, null, privateKey);
        }
        _manager.Compact(remote, privateKey, threshold: 5, keepRecent: 2);

        // Local has Seq 0-4. Remote has Snapshot (up to Seq 7) + Seq 8-9.
        int added = _manager.MergeManifest(local, remote, publicKey);

        Assert.Equal(2, added);
        Assert.NotNull(local.Snapshot);
        Assert.Equal(7, local.Snapshot.LastSequenceNumber);
        Assert.Equal(2, local.Operations.Count);
        Assert.Equal(8, local.Operations[0].SequenceNumber);
        Assert.True(_manager.VerifyManifest(local, publicKey));
    }
}
