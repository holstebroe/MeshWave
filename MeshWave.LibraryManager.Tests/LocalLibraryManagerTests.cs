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



    [Fact]
    public void IndexLibrary_IncludesMissingFilesWithMetadataAsNotDownloaded()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var cacheDir = Path.Combine(tempDir, "Artist", "Album", ".cache");
        Directory.CreateDirectory(cacheDir);

        var originalFile = Path.Combine(tempDir, "Artist", "Album", "Song.mp3");
        var metaFile = Path.Combine(cacheDir, "Song.meta.json");

        // We will construct the JSON via an object to avoid string escaping issues
        var cacheData = new {
            Title = "Missing Song",
            Artist = "Artist",
            Album = "Album",
            DurationSeconds = 120.0,
            SourceLastWriteUtc = System.DateTime.UtcNow,
            OriginalFilePath = originalFile,
            ContentHash = "hash123"
        };
        var cacheContent = System.Text.Json.JsonSerializer.Serialize(cacheData);
        File.WriteAllText(metaFile, cacheContent);

        var manager = new LocalLibraryManager(tempDir);

        // Act
        manager.IndexLibrary();

        // Assert
        var tracks = manager.GetAllTracks().ToList();
        Assert.Single(tracks);
        Assert.Equal("Missing Song", tracks[0].Title);
        Assert.False(tracks[0].IsDownloaded);
        Assert.Equal("hash123", tracks[0].ContentHash);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void IndexLibrary_PreservesTrackIdAndUpdatesPath_WhenFolderIsRenamed()
    {
        // Arrange
        var sourceDir = Path.GetFullPath("../../../../TestData/John/RockPlastic");
        if (!Directory.Exists(sourceDir))
        {
            var current = Directory.GetCurrentDirectory();
            while (current != null && !Directory.Exists(Path.Combine(current, "TestData")))
            {
                current = Directory.GetParent(current)?.FullName;
            }
            if (current != null)
                sourceDir = Path.Combine(current, "TestData", "John", "RockPlastic");
        }
        var sourceFile = Directory.GetFiles(sourceDir, "*.mp3").FirstOrDefault();
        Assert.NotNull(sourceFile);
        var myMusicBaseFolder = Path.Combine(_tempDirectory, "MyMusic");

        LocalLibraryManager.ImportSingleFileToOrganizedStructure(sourceFile, myMusicBaseFolder);

        var libraryManager = new LocalLibraryManager(myMusicBaseFolder);
        libraryManager.IndexLibrary();

        var originalTracks = libraryManager.GetAllTracks().ToList();
        Assert.Single(originalTracks);
        var originalTrackId = originalTracks[0].TrackId;
        var originalAlbumId = originalTracks[0].AlbumId;
        var originalFilePath = originalTracks[0].FilePath;

        // Act - Rename the folder
        var artistFolder = Directory.GetDirectories(myMusicBaseFolder)[0];
        var newArtistFolder = Path.Combine(myMusicBaseFolder, "Renamed Artist");
        Directory.Move(artistFolder, newArtistFolder);

        // Rescan
        libraryManager.IndexLibrary();

        // Assert
        var rescannedTracks = libraryManager.GetAllTracks().ToList();
        Assert.Single(rescannedTracks);

        var rescannedTrack = rescannedTracks[0];
        Assert.Equal(originalTrackId, rescannedTrack.TrackId); // TrackId must remain stable
        Assert.Equal(originalAlbumId, rescannedTrack.AlbumId); // AlbumId must remain stable
        Assert.NotEqual(originalFilePath, rescannedTrack.FilePath); // Path should be updated
        Assert.StartsWith(newArtistFolder, rescannedTrack.FilePath); // It must be in the new directory
    }
}