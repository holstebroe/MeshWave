using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.TestUtilities;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class PeerManifestStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"PeerManifestStoreTests_{Guid.NewGuid():N}");
    private readonly ManifestManager _manager = new();
    private readonly PeerManifestStore _store;
    private readonly DummyEnvironment _environment;

    public PeerManifestStoreTests()
    {
        _environment = new DummyEnvironment(_tempDir);
        _store = new PeerManifestStore(_environment, _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static (string publicKeyPem, string privateKeyPem) GenerateKeyPair()
    {
        var (priv, pub) = CryptoService.GenerateKeyPair();
        return (pub, priv);
    }

    private Manifest BuildSignedManifest(string userId, string privateKeyPem)
    {
        var manifest = _manager.CreateManifest(userId);
        _manager.AppendSignedOperation(manifest, ManifestOperationType.Create,
            "track-1", "Track", "hash-abc", null, privateKeyPem);
        return manifest;
    }

    // ─── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void Get_ReturnsNull_WhenNoManifestCached()
    {
        Assert.Null(_store.Get("unknown-user"));
    }

    [Fact]
    public void MergeAndSave_ReturnsMergedCount_AndCachesManifest()
    {
        var (pub, priv) = GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
        var incoming = BuildSignedManifest(userId, priv);

        var added = _store.MergeAndSave(incoming, pub, _manager);

        Assert.Equal(1, added);
        Assert.NotNull(_store.Get(userId));
    }

    [Fact]
    public void MergeAndSave_PersistsToDisk()
    {
        var (pub, priv) = GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
        var incoming = BuildSignedManifest(userId, priv);

        _store.MergeAndSave(incoming, pub, _manager);

        // A file should exist in the temp directory
        var files = Directory.GetFiles(_tempDir, "*.json");
        Assert.Single(files);
    }

    [Fact]
    public void LoadAll_RestoresPersistedManifests()
    {
        var (pub, priv) = GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
        var incoming = BuildSignedManifest(userId, priv);

        _store.MergeAndSave(incoming, pub, _manager);

        // Create a fresh store pointing at the same directory
        var store2 = new PeerManifestStore(_environment, _tempDir);
        store2.LoadAll();

        var loaded = store2.Get(userId);
        Assert.NotNull(loaded);
        Assert.Equal(userId, loaded!.UserId);
        Assert.Single(loaded.Operations);
    }

    [Fact]
    public void MergeAndSave_IdempotentForSameOperations()
    {
        var (pub, priv) = GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
        var incoming = BuildSignedManifest(userId, priv);

        var first = _store.MergeAndSave(incoming, pub, _manager);
        var second = _store.MergeAndSave(incoming, pub, _manager); // same data again

        Assert.Equal(1, first);
        Assert.Equal(0, second); // already merged
    }

    [Fact]
    public void MergeAndSave_RejectsManifestWithWrongPublicKey()
    {
        var (pub, priv) = GenerateKeyPair();
        var (wrongPub, _) = GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
        var incoming = BuildSignedManifest(userId, priv);

        // Wrong public key — signature verification should fail; 0 ops added
        var added = _store.MergeAndSave(incoming, wrongPub, _manager);

        Assert.Equal(0, added);
    }

    [Fact]
    public void Remove_DeletesCacheEntryAndDiskFile()
    {
        var (pub, priv) = GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
        var incoming = BuildSignedManifest(userId, priv);
        _store.MergeAndSave(incoming, pub, _manager);

        _store.Remove(userId);

        Assert.Null(_store.Get(userId));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.json"));
    }

    [Fact]
    public void GetAll_ReturnsAllCachedManifests()
    {
        for (var i = 0; i < 3; i++)
        {
            var (pub, priv) = GenerateKeyPair();
            var userId = CryptoService.DeriveUserIdFromPublicKey(pub);
            var incoming = BuildSignedManifest(userId, priv);
            _store.MergeAndSave(incoming, pub, _manager);
        }

        Assert.Equal(3, _store.GetAll().Count);
    }
}
