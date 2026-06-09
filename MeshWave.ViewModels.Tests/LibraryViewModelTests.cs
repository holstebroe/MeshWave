using Moq;
using Xunit;
using MeshWave.Wpf.ViewModels;
using MeshWave.LibraryManager;
using System.IO;

namespace MeshWave.ViewModels.Tests;

public class LibraryViewModelTests
{
    [Fact]
    public void LoadLibrary_SetsIsDownloadedFalse_AndAllowsReDownload_ForMissingFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var cacheDir = Path.Combine(tempDir, "Artist", "Album", ".cache");
        Directory.CreateDirectory(cacheDir);

        var originalFile = Path.Combine(tempDir, "Artist", "Album", "Song.mp3");
        var metaFile = Path.Combine(cacheDir, "Song.meta.json");

        var json = "{\n" +
            "\"Title\": \"Missing Song\",\n" +
            "\"Artist\": \"Artist\",\n" +
            "\"Album\": \"Album\",\n" +
            "\"DurationSeconds\": 120,\n" +
            "\"SourceLastWriteUtc\": \"2023-01-01T00:00:00Z\",\n" +
            "\"OriginalFilePath\": \"" + originalFile.Replace("\\", "\\\\") + "\",\n" +
            "\"ContentHash\": \"hash123\"\n" +
        "}";

        File.WriteAllText(metaFile, json);

        // Use a mock ApplicationViewModel to allow testing ReDownloadTrackCommand
        var mockAppVm = new Mock<ApplicationViewModel>(null, null, null, null, null, null, null);
        var vm = new LibraryViewModel(mockAppVm.Object, isMyMusicLibrary: true);

        // Act
        vm.LoadLibrary(tempDir);

        // Assert
        Assert.Single(vm.Tracks);
        var track = vm.Tracks[0];

        Assert.Equal("Missing Song", track.Title);
        Assert.False(track.IsDownloaded);
        Assert.Equal("hash123", track.ContentHash);
        Assert.Equal("Not Downloaded", track.StatusBadge);
        Assert.False(track.CanPlay);

        // Ensure download command is executable
        Assert.True(vm.ReDownloadTrackCommand.CanExecute(track));

        // Cleanup
        vm.Dispose();
        Directory.Delete(tempDir, true);
    }
}
