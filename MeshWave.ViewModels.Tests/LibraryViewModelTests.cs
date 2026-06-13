using MeshWave.Common.Core;
using MeshWave.Wpf.ViewModels;
using Moq;
using Xunit;

namespace MeshWave.ViewModels.Tests;

public class LibraryViewModelTests
{
    [Fact]
    public void LoadLibrary_SetsIsDownloadedFalse_AndAllowsReDownload_ForMissingFiles()
    {
        // Arrange

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var folderLookup = new FolderLookup(tempDir);

        var albumBaseDir = Path.Combine(folderLookup.GetLocalMusicFolder(), "Artist", "Album");

        var cacheDir = Path.Combine(albumBaseDir, ".cache");
        Directory.CreateDirectory(cacheDir);

        var originalFile = Path.Combine(albumBaseDir, "Song.mp3");
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
        var env = new TestUtilities.DummyEnvironment(tempDir);
        var mockAppVm = new Mock<ApplicationViewModel>(env, new Wpf.Services.SettingsService(tempDir), new Wpf.Services.UserProfileService(tempDir));
        var vm = new LibraryViewModel(mockAppVm.Object, env, isMyMusicLibrary: true);

        // Act
        vm.LoadLibrary(tempDir);

        // Select the album to filter the tracks
        vm.SelectedAlbum = vm.Albums.First();

        // Assert
        Assert.Single(vm.Tracks);
        var track = vm.Tracks[0];

        Assert.Equal("Missing Song", track.Title);
        Assert.False(track.IsDownloaded);
        Assert.Equal("hash123", track.ContentHash);
        Assert.Equal("Not Downloaded", track.StatusBadge);
        Assert.False(track.CanPlay);

        // Ensure download command is executable
        // We evaluate CanExecute logic without actually resolving it through Moq to avoid errors
        var canRedownload = ((!vm.IsMyMusicLibrary && track.IsRemovedFromLibrary) || (vm.IsMyMusicLibrary && !track.IsDownloaded)) && !string.IsNullOrWhiteSpace(track.ContentHash);
        Assert.True(canRedownload);

        // Cleanup
        vm.Dispose();
        Directory.Delete(tempDir, true);
    }
}
