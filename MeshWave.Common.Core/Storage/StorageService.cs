using MeshWave.Common.Core.Crypto;

namespace MeshWave.Common.Core.Storage;

/// <summary>
/// Provides abstraction for local storage operations.
/// Manages content-addressed blobs (by SHA256 hash).
/// </summary>
public class StorageService
{
    private readonly string _basePath;
    private readonly string _blobsPath;
    private readonly string _metadataPath;

    public StorageService(string basePath)
    {
        _basePath = basePath;
        _blobsPath = Path.Combine(_basePath, "blobs");
        _metadataPath = Path.Combine(_basePath, "metadata");

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(_blobsPath);
        Directory.CreateDirectory(_metadataPath);
    }

    /// <summary>
    /// Stores a file blob using content-addressed storage (by file hash).
    /// Returns the hash of the stored content.
    /// </summary>
    public string StoreBlob(byte[] content, string? proposedHash = null)
    {
        var hash = proposedHash ?? CryptoService.ComputeHash(content);
        var blobPath = Path.Combine(_blobsPath, hash);

        if (!File.Exists(blobPath)) File.WriteAllBytes(blobPath, content);

        return hash;
    }

    /// <summary>
    /// Stores a file blob from disk.
    /// Returns the hash of the stored content.
    /// </summary>
    public string StoreBlobFromFile(string sourceFilePath)
    {
        var content = File.ReadAllBytes(sourceFilePath);
        return StoreBlob(content);
    }

    /// <summary>
    /// Retrieves a blob by hash.
    /// </summary>
    public byte[]? GetBlob(string hash)
    {
        var blobPath = Path.Combine(_blobsPath, hash);
        if (File.Exists(blobPath)) return File.ReadAllBytes(blobPath);

        return null;
    }

    /// <summary>
    /// Retrieves a blob and writes it to a file.
    /// </summary>
    public bool ExtractBlobToFile(string hash, string destinationPath)
    {
        var blob = GetBlob(hash);
        if (blob == null)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllBytes(destinationPath, blob);
        return true;
    }

    /// <summary>
    /// Checks if a blob exists.
    /// </summary>
    public bool BlobExists(string hash)
    {
        var blobPath = Path.Combine(_blobsPath, hash);
        return File.Exists(blobPath);
    }

    /// <summary>
    /// Stores metadata JSON for an entity.
    /// </summary>
    public void StoreMetadata(string entityId, string metadata)
    {
        var metadataFile = Path.Combine(_metadataPath, $"{entityId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataFile)!);
        File.WriteAllText(metadataFile, metadata);
    }

    /// <summary>
    /// Retrieves metadata JSON for an entity.
    /// </summary>
    public string? GetMetadata(string entityId)
    {
        var metadataFile = Path.Combine(_metadataPath, $"{entityId}.json");
        if (File.Exists(metadataFile)) return File.ReadAllText(metadataFile);

        return null;
    }

    /// <summary>
    /// Gets the size of a blob in bytes.
    /// </summary>
    public long? GetBlobSize(string hash)
    {
        var blobPath = Path.Combine(_blobsPath, hash);
        if (File.Exists(blobPath)) return new FileInfo(blobPath).Length;

        return null;
    }

    /// <summary>
    /// Lists all blob hashes.
    /// </summary>
    public IEnumerable<string> ListBlobs()
    {
        if (!Directory.Exists(_blobsPath))
            return Enumerable.Empty<string>();

        return Directory.EnumerateFiles(_blobsPath)
            .Select(f => Path.GetFileName(f));
    }

    /// <summary>
    /// Deletes a blob by hash.
    /// </summary>
    public bool DeleteBlob(string hash)
    {
        var blobPath = Path.Combine(_blobsPath, hash);
        if (File.Exists(blobPath))
        {
            File.Delete(blobPath);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the total size of all stored blobs in bytes.
    /// </summary>
    public long GetTotalBlobSize()
    {
        if (!Directory.Exists(_blobsPath))
            return 0;

        return new DirectoryInfo(_blobsPath).EnumerateFiles()
            .Sum(f => f.Length);
    }
}
