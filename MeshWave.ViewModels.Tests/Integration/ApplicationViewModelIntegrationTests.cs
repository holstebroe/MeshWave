using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using MeshWave.ViewModels;
using Moq;
using Xunit;

namespace MeshWave.ViewModels.Tests.Integration;

public class ApplicationViewModelIntegrationTests : IAsyncLifetime
{
    private MeshTestContext _context = null!;

    public Task InitializeAsync()
    {
        _context = new MeshTestContext();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task P2PConnectionLifecycleScenario()
    {
        // For ApplicationViewModel to work in tests, we need to mock its dependencies
        // to avoid loading real user settings and playing real audio.
        var tempAppData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var mockSettings = new Mock<Services.SettingsService>(tempAppData);
        mockSettings.Setup(s => s.LoadSettings()).Returns(new Models.AppSettings
        {
            BaseFolder = Path.Combine(tempAppData, "Music"),
            P2P = new Models.P2PSettings { Enabled = false }
        });

        var mockAudio = new Mock<Services.IAudioPlaybackService>();

        var appVm = new ApplicationViewModel(
            settingsService: mockSettings.Object,
            audioServiceFactory: () => mockAudio.Object);
        var orchestrator = appVm.SyncOrchestrator;

        Assert.False(appVm.P2PIsConnected);
        Assert.Equal(orchestrator.ConnectedPeerCount, appVm.P2PPeerCount);
    }

    [Fact]
    public async Task NavigationScenario()
    {
        var tempAppData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var mockSettings = new Mock<Services.SettingsService>(tempAppData);
        mockSettings.Setup(s => s.LoadSettings()).Returns(new Models.AppSettings
        {
            BaseFolder = Path.Combine(tempAppData, "Music"),
            P2P = new Models.P2PSettings { Enabled = false }
        });

        var mockAudio = new Mock<Services.IAudioPlaybackService>();

        var appVm = new ApplicationViewModel(
            settingsService: mockSettings.Object,
            audioServiceFactory: () => mockAudio.Object);

        appVm.NavigateToHome();
        Assert.IsType<HomeViewModel>(appVm.CurrentViewModel);

        appVm.NavigateToLibrary();
        Assert.IsType<LibraryViewModel>(appVm.CurrentViewModel);
        Assert.False(((LibraryViewModel)appVm.CurrentViewModel).IsMyMusicLibrary);

        appVm.NavigateToMyMusic();
        Assert.IsType<LibraryViewModel>(appVm.CurrentViewModel);
        Assert.True(((LibraryViewModel)appVm.CurrentViewModel).IsMyMusicLibrary);

        appVm.NavigateToSettings();
        Assert.IsType<SettingsViewModel>(appVm.CurrentViewModel);

        appVm.NavigateToBrowse();
        Assert.IsType<BrowseViewModel>(appVm.CurrentViewModel);

        appVm.NavigateToCommunity();
        Assert.IsType<CommunityViewModel>(appVm.CurrentViewModel);
    }
}
