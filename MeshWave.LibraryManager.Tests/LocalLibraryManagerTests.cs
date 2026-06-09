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
    public void ImportSingleFileToOrganizedStructure_CreatesMeshwaveIdFiles()
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

        // Act
        var result = LocalLibraryManager.ImportSingleFileToOrganizedStructure(sourceFile, myMusicBaseFolder);

        // Assert
        Assert.True(result);

        var dirs = Directory.GetDirectories(myMusicBaseFolder);
        Assert.NotEmpty(dirs);
        var artistFolder = dirs[0];

        var albumDirs = Directory.GetDirectories(artistFolder);
        Assert.NotEmpty(albumDirs);
        var albumFolder = albumDirs[0];

        var artistIdPath = Path.Combine(artistFolder, ".meshwave-id");
        var albumIdPath = Path.Combine(albumFolder, ".meshwave-id");

        Assert.True(File.Exists(artistIdPath));
        Assert.True(File.Exists(albumIdPath));

        var artistIdContent = File.ReadAllText(artistIdPath);
        var albumIdContent = File.ReadAllText(albumIdPath);

        Assert.True(Guid.TryParse(artistIdContent, out _));
        Assert.True(Guid.TryParse(albumIdContent, out _));

        // File should be hidden
        Assert.True(File.GetAttributes(artistIdPath).HasFlag(FileAttributes.Hidden));
        Assert.True(File.GetAttributes(albumIdPath).HasFlag(FileAttributes.Hidden));
    }
}
