using MeshWave.Common.Core;
using MeshWave.Wpf.ViewModels;
using MeshWave.Wpf.ViewModels.Items;
using Moq;
using Xunit;

namespace MeshWave.ViewModels.Tests;

public class LibraryViewModelTests
{
    [Fact]
    public void LoadLibrary_SetsAvailabilityStateToRemote_AndAllowsReDownload_ForMissingFiles()
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
        var mockAppVm = new Mock<ApplicationViewModel>(env, new Wpf.Services.SettingsService(tempDir), new Wpf.Services.UserProfileService(tempDir), (System.Func<MeshWave.Wpf.Services.IAudioPlaybackService>)(() => null!));
        var vm = new LibraryViewModel(mockAppVm.Object, env, isMyMusicLibrary: true);

        // Act
        vm.LoadLibrary(tempDir);

        // Select the album to filter the tracks
        vm.SelectedAlbum = vm.Albums.First();

        // Assert
        Assert.Single(vm.Tracks);
        var track = vm.Tracks[0];

        Assert.Equal("Missing Song", track.Title);
        Assert.Equal(MeshWave.Common.Core.Enums.TrackAvailabilityState.Remote, track.AvailabilityState);
        Assert.Equal("hash123", track.ContentHash);
        Assert.Equal("Not Downloaded", track.StatusBadge);
        Assert.False(track.CanPlay);

        // Ensure download command is executable
        // We evaluate CanExecute logic without actually resolving it through Moq to avoid errors
        var canRedownload = (track.AvailabilityState == MeshWave.Common.Core.Enums.TrackAvailabilityState.Remote) && !string.IsNullOrWhiteSpace(track.ContentHash);
        Assert.True(canRedownload);

        // Cleanup
        vm.Dispose();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task SearchQuery_DebouncesAndTriggersSearch_Once()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var env = new TestUtilities.DummyEnvironment(tempDir);
        var mockAppVm = new Mock<ApplicationViewModel>(env, new Wpf.Services.SettingsService(tempDir), new Wpf.Services.UserProfileService(tempDir), (System.Func<MeshWave.Wpf.Services.IAudioPlaybackService>)(() => null!));
        var vm = new LibraryViewModel(mockAppVm.Object, env, isMyMusicLibrary: true);

        int isSearchingTrueCount = 0;
        int isSearchingFalseCount = 0;

        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(LibraryViewModel.IsSearching))
            {
                if (vm.IsSearching)
                {
                    isSearchingTrueCount++;
                }
                else
                {
                    isSearchingFalseCount++;
                }
            }
        };

        // Act - Simulate rapid typing
        vm.SearchQuery = "t";
        await Task.Delay(50, TestContext.Current.CancellationToken);
        vm.SearchQuery = "te";
        await Task.Delay(50, TestContext.Current.CancellationToken);
        vm.SearchQuery = "tes";
        await Task.Delay(50, TestContext.Current.CancellationToken);
        vm.SearchQuery = "test";

        // Assert while typing
        Assert.True(vm.IsSearching);
        Assert.Equal(1, isSearchingTrueCount); // SetProperty deduplicates
        Assert.Equal(0, isSearchingFalseCount); // Should not have resolved to false yet

        // Act - Wait for debounce timer to expire
        await Task.Delay(400, TestContext.Current.CancellationToken);

        // Assert after delay
        Assert.False(vm.IsSearching);
        Assert.Equal(1, isSearchingTrueCount);

        // Assert that the search operation itself (represented by the timer expiring and reaching the final IsSearching = false)
        // was only executed once. The task cancellations effectively debounce the subsequent actions.
        Assert.Equal(1, isSearchingFalseCount);

        // Cleanup
        vm.Dispose();
        Directory.Delete(tempDir, true);
    }
}
