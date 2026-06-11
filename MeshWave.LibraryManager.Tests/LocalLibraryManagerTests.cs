using System.Text.Json;
using Xunit;

namespace MeshWave.LibraryManager.Tests;

public class LocalLibraryManagerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LocalLibraryManager _libraryManager;

    public LocalLibraryManagerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"meshwave_lib_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
        _libraryManager = new LocalLibraryManager(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void Constructor_AcceptsBasePath()
    {
        // Act & Assert - constructor should not throw
        Assert.NotNull(_libraryManager);
    }

    [Fact]
    public void GetAllTracks_ReturnsEmptyList_WhenNoTracksIndexed()
    {
        // Act
        var tracks = _libraryManager.GetAllTracks().ToList();

        // Assert
        Assert.Empty(tracks);
    }

    [Fact]
    public void GetAllAlbums_ReturnsEmptyList_WhenNoAlbumsIndexed()
    {
        // Act
        var albums = _libraryManager.GetAllAlbums().ToList();

        // Assert
        Assert.Empty(albums);
    }

    [Fact]
    public void IndexLibrary_ExecutesWithoutError()
    {
        // Act & Assert - should not throw
        _libraryManager.IndexLibrary();
    }

    //[Fact]
    //public void IndexLibrary_IncludesMissingFilesWithMetadataAsNotDownloaded()
    //{
    //    // Arrange
    //    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    //    Directory.CreateDirectory(tempDir);
    //    var artistJsonDoc = System.Text.Json.JsonDocument.Parse(artistIdContent);
    //    var albumJsonDoc = System.Text.Json.JsonDocument.Parse(albumIdContent);

    //    Assert.True(artistJsonDoc.RootElement.TryGetProperty("EntityId", out var artistEntityIdProp));
    //    Assert.Equal("local", artistEntityIdProp.GetString());

    //    Assert.True(artistJsonDoc.RootElement.TryGetProperty("Id", out var artistIdProp));
    //    Assert.True(Guid.TryParse(artistIdProp.GetString(), out _));

    //    Assert.True(albumJsonDoc.RootElement.TryGetProperty("EntityId", out var albumEntityIdProp));
    //    Assert.False(string.IsNullOrWhiteSpace(albumEntityIdProp.GetString()));

    //    Assert.True(albumJsonDoc.RootElement.TryGetProperty("Id", out var albumIdProp));
    //    Assert.True(Guid.TryParse(albumIdProp.GetString(), out _));

    //    var cacheDir = Path.Combine(tempDir, "Artist", "Album", ".cache");
    //    Directory.CreateDirectory(cacheDir);

    //    var originalFile = Path.Combine(tempDir, "Artist", "Album", "Song.mp3");
    //    var metaFile = Path.Combine(cacheDir, "Song.meta.json");

    //    // We will construct the JSON via an object to avoid string escaping issues
    //    var cacheData = new {
    //        Title = "Missing Song",
    //        Artist = "Artist",
    //        Album = "Album",
    //        DurationSeconds = 120.0,
    //        SourceLastWriteUtc = System.DateTime.UtcNow,
    //        OriginalFilePath = originalFile,
    //        ContentHash = "hash123"
    //    };
    //    var cacheContent = System.Text.Json.JsonSerializer.Serialize(cacheData);
    //    File.WriteAllText(metaFile, cacheContent);

    //    var manager = new LocalLibraryManager(tempDir);

    //    // Act
    //    manager.IndexLibrary();

    //    // Assert
    //    var tracks = manager.GetAllTracks().ToList();
    //    Assert.Single(tracks);
    //    Assert.Equal("Missing Song", tracks[0].Title);
    //    Assert.False(tracks[0].IsDownloaded);
    //    Assert.Equal("hash123", tracks[0].ContentHash);

    //    // Cleanup
    //    Directory.Delete(tempDir, true);
    //}

}