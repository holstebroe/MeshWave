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


}
