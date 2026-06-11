using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
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

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);

        Assert.Single(manifest.Operations);
        Assert.Equal(0, manifest.Operations[0].SequenceNumber);
        Assert.NotEmpty(manifest.Operations[0].Signature);
    }

    [Fact]
    public void VerifyManifest_ReturnsTrueForValidSignatures()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Update, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash456", FileSize = 0 } } }, null, privateKey);

        Assert.True(_manager.VerifyManifest(manifest, publicKey));
    }

    [Fact]
    public void VerifyManifest_ReturnsFalseForTamperedOperation()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);

        // Tamper with the content hash after signing
        manifest.Operations[0].AudioVersions = new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "tampered-hash", FileSize = 0 } } };

        Assert.False(_manager.VerifyManifest(manifest, publicKey));
    }

    [Fact]
    public void VerifyManifest_ReturnsFalseForWrongPublicKey()
    {
        var (privateKey, _) = CryptoService.GenerateKeyPair();
        var (_, otherPublicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);

        Assert.False(_manager.VerifyManifest(manifest, otherPublicKey));
    }

    [Fact]
    public void VerifyManifest_ReturnsFalseForOutOfOrderSequence()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-1");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-2", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash456", FileSize = 0 } } }, null, privateKey);

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
        _manager.AppendSignedOperation(remote, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);
        _manager.AppendSignedOperation(remote, ManifestOperationType.Create, "track-2", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash456", FileSize = 0 } } }, null, privateKey);

        var added = _manager.MergeManifest(local, remote, publicKey);

        Assert.Equal(2, added);
        Assert.Equal(2, local.Operations.Count);
    }

    [Fact]
    public void MergeManifest_SkipsAlreadyPresentOperations()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        var manifest = _manager.CreateManifest("user-2");
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } }, null, privateKey);

        // Merge the same manifest into itself (no new ops)
        var added = _manager.MergeManifest(manifest, manifest, publicKey);

        Assert.Equal(0, added);
        Assert.Single(manifest.Operations);
    }

    [Fact]
    public void VerifyManifest_WorksWithDeltas()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var manifest = _manager.CreateManifest("user-delta");

        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-0", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash0", FileSize = 0 } } }, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-1", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash1", FileSize = 0 } } }, null, privateKey);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create, "track-2", "Track", new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash2", FileSize = 0 } } }, null, privateKey);

        // Create a delta manifest containing only seq 1 and 2
        var delta = new Manifest
        {
            UserId = manifest.UserId,
            Operations = manifest.Operations.Skip(1).ToList(),
            Version = manifest.Version,
            LastUpdated = manifest.LastUpdated
        };

        Assert.True(_manager.VerifyManifest(delta, publicKey));
    }

    [Fact]
    public void MergeManifest_ThrowsForDifferentUser()
    {
        var localManifest = _manager.CreateManifest("user-A");
        var remoteManifest = _manager.CreateManifest("user-B");

        Assert.Throws<ArgumentException>(() =>
            _manager.MergeManifest(localManifest, remoteManifest, "any-key"));
    }

    [Fact]
    public void MergeManifest_DropsOperations_WhenStringsExceedLimit()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        var local = _manager.CreateManifest("user-3");
        var remote = _manager.CreateManifest("user-3");

        // Valid operation
        _manager.AppendSignedOperation(remote, ManifestOperationType.Create,
            "track-1", "Track", "hash123", null, privateKey);

        // Invalid operation (exceeds limit)
        var invalidOp = new ManifestOperation
        {
            OperationId = new string('A', SecurityLimits.MaxOperationIdLength + 1),
            OperationType = ManifestOperationType.Create,
            TargetId = "track-2",
            TargetType = "Track",
            SequenceNumber = 1,
            Timestamp = System.DateTime.UtcNow,
            Signature = "" // Set it properly
        };
        var signable = ManifestManager.BuildSignablePayload(invalidOp);
        invalidOp.Signature = CryptoService.SignData(signable, privateKey);
        remote.Operations.Add(invalidOp);

        // Act
        var added = _manager.MergeManifest(local, remote, publicKey);

        // Assert
        Assert.Equal(1, added);
        Assert.Single(local.Operations);
        Assert.Equal("track-1", local.Operations[0].TargetId);
    }
}
