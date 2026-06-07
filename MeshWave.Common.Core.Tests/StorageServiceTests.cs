using System.Text;
using MeshWave.Common.Core.Storage;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class StorageServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly StorageService _storageService;

    public StorageServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"meshwave_test_{Guid.NewGuid()}");
        _storageService = new StorageService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void Constructor_CreatesRequiredDirectories()
    {
        // Assert
        Assert.True(Directory.Exists(_tempDirectory));
        Assert.True(Directory.Exists(Path.Combine(_tempDirectory, "blobs")));
        Assert.True(Directory.Exists(Path.Combine(_tempDirectory, "metadata")));
    }

    [Fact]
    public void StoreBlob_StoresContentAndReturnsHash()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test blob content");

        // Act
        var hash = _storageService.StoreBlob(content);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(_storageService.BlobExists(hash));
    }

    [Fact]
    public void GetBlob_ReturnsStoredContent()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test blob content");
        var hash = _storageService.StoreBlob(content);

        // Act
        var retrievedContent = _storageService.GetBlob(hash);

        // Assert
        Assert.NotNull(retrievedContent);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public void GetBlob_ReturnsNull_ForNonExistentBlob()
    {
        // Act
        var result = _storageService.GetBlob("nonexistent_hash");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void BlobExists_ReturnsTrueForExistingBlob()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test content");
        var hash = _storageService.StoreBlob(content);

        // Act
        var exists = _storageService.BlobExists(hash);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void BlobExists_ReturnsFalseForNonExistentBlob()
    {
        // Act
        var exists = _storageService.BlobExists("nonexistent_hash");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void StoreBlobFromFile_StoresFileAndReturnsHash()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.txt");
        File.WriteAllText(tempFile, "Test file content");

        try
        {
            // Act
            var hash = _storageService.StoreBlobFromFile(tempFile);

            // Assert
            Assert.NotEmpty(hash);
            Assert.True(_storageService.BlobExists(hash));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExtractBlobToFile_WritesBlobToFile()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test blob content");
        var hash = _storageService.StoreBlob(content);
        var outputFile = Path.Combine(Path.GetTempPath(), $"extracted_{Guid.NewGuid()}.txt");

        try
        {
            // Act
            var success = _storageService.ExtractBlobToFile(hash, outputFile);

            // Assert
            Assert.True(success);
            Assert.True(File.Exists(outputFile));
            var fileContent = File.ReadAllBytes(outputFile);
            Assert.Equal(content, fileContent);
        }
        finally
        {
            if (File.Exists(outputFile))
                File.Delete(outputFile);
        }
    }

    [Fact]
    public void GetBlobSize_ReturnsCorrectSize()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test blob content");
        var hash = _storageService.StoreBlob(content);

        // Act
        var size = _storageService.GetBlobSize(hash);

        // Assert
        Assert.NotNull(size);
        Assert.Equal(content.Length, size.Value);
    }

    [Fact]
    public void GetBlobSize_ReturnsNull_ForNonExistentBlob()
    {
        // Act
        var size = _storageService.GetBlobSize("nonexistent_hash");

        // Assert
        Assert.Null(size);
    }

    [Fact]
    public void StoreMetadata_StoresJsonMetadata()
    {
        // Arrange
        var entityId = "test-entity-1";
        var metadata = @"{ ""name"": ""Test"", ""value"": 42 }";

        // Act
        _storageService.StoreMetadata(entityId, metadata);

        // Assert
        var retrieved = _storageService.GetMetadata(entityId);
        Assert.NotNull(retrieved);
        Assert.Equal(metadata, retrieved);
    }

    [Fact]
    public void GetMetadata_ReturnsNull_ForNonExistentEntity()
    {
        // Act
        var result = _storageService.GetMetadata("nonexistent-entity");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void DeleteBlob_RemovesBlobSuccessfully()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test content");
        var hash = _storageService.StoreBlob(content);

        // Act
        var deleted = _storageService.DeleteBlob(hash);

        // Assert
        Assert.True(deleted);
        Assert.False(_storageService.BlobExists(hash));
    }

    [Fact]
    public void GetTotalBlobSize_ReturnsCorrectTotal()
    {
        // Arrange
        var content1 = Encoding.UTF8.GetBytes("Content 1");
        var content2 = Encoding.UTF8.GetBytes("Content 2 longer");
        _storageService.StoreBlob(content1);
        _storageService.StoreBlob(content2);

        // Act
        var totalSize = _storageService.GetTotalBlobSize();

        // Assert
        Assert.Equal(content1.Length + content2.Length, totalSize);
    }

    [Fact]
    public void ListBlobs_ReturnsAllStoredBlobs()
    {
        // Arrange
        var hash1 = _storageService.StoreBlob(Encoding.UTF8.GetBytes("Content 1"));
        var hash2 = _storageService.StoreBlob(Encoding.UTF8.GetBytes("Content 2"));

        // Act
        var blobs = _storageService.ListBlobs().ToList();

        // Assert
        Assert.Contains(hash1, blobs);
        Assert.Contains(hash2, blobs);
        Assert.True(blobs.Count >= 2);
    }
}
