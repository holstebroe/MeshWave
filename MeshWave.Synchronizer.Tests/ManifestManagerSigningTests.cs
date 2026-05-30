using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class ManifestManagerSigningTests
{
    private readonly ManifestManager _manager = new();

    [Fact]
    public void AppendSignedOperation_CreatesVerifiableOperation()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);

        Assert.Single(manifest.Operations);
        Assert.Equal(0, manifest.Operations[0].SequenceNumber);
        Assert.NotEmpty(manifest.Operations[0].Signature);
    }

    [Fact]
    public void VerifyManifest_ReturnsTrueForValidSignatures()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Update,
            "track-1", "Track", "hash456", null, privateKey);

        Assert.True(_manager.VerifyManifest(manifest, publicKey));
    }

    [Fact]
    public void VerifyManifest_ReturnsFalseForTamperedOperation()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);

        // Tamper with the content hash after signing
        manifest.Operations[0].ContentHash = "tampered-hash";

        Assert.False(_manager.VerifyManifest(manifest, publicKey));
    }

    [Fact]
    public void VerifyManifest_ReturnsFalseForWrongPublicKey()
    {
        var (privateKey, _) = CryptoService.GenerateKeyPair();
        var (_, otherPublicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);

        Assert.False(_manager.VerifyManifest(manifest, otherPublicKey));
    }

    [Fact]
    public void VerifyManifest_ReturnsFalseForOutOfOrderSequence()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-2", "Track", "hash456", null, privateKey);

        // Swap sequence numbers to simulate tampering
        manifest.Operations[0].SequenceNumber = 1;
        manifest.Operations[1].SequenceNumber = 0;

        Assert.False(_manager.VerifyManifest(manifest, publicKey));
    }

    [Fact]
    public void MergeManifest_AddsNewVerifiedOperations()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        var local = _manager.CreateManifest("user-2");

        var remote = _manager.CreateManifest("user-2");
        _manager.AppendSignedOperation(remote, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);
        _manager.AppendSignedOperation(remote, ManifestOperationType.Create,
            "track-2", "Track", "hash456", null, privateKey);

        var added = _manager.MergeManifest(local, remote, publicKey);

        Assert.Equal(2, added);
        Assert.Equal(2, local.Operations.Count);
    }

    [Fact]
    public void MergeManifest_SkipsAlreadyPresentOperations()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        var manifest = _manager.CreateManifest("user-2");
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);

        // Merge the same manifest into itself (no new ops)
        var added = _manager.MergeManifest(manifest, manifest, publicKey);

        Assert.Equal(0, added);
        Assert.Single(manifest.Operations);
    }

    [Fact]
    public void MergeManifest_ThrowsForDifferentUser()
    {
        var localManifest = _manager.CreateManifest("user-A");
        var remoteManifest = _manager.CreateManifest("user-B");

        Assert.Throws<ArgumentException>(() =>
            _manager.MergeManifest(localManifest, remoteManifest, "any-key"));
    }
}
