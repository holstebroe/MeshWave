using MeshWave.Models;
using MeshWave.Services;
using MeshWave.TestUtilities;
using Moq;
using Xunit;

namespace MeshWave.ViewModels.Tests.Integration;

public class ApplicationViewModelIntegrationTests : IAsyncLifetime
{
    private MeshTestContext _context = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await MeshTestStandardScenarios.CreateSingleUserScenario();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public void P2PConnectionLifecycleScenario()
    {
        // For ApplicationViewModel to work in tests, we need to mock its dependencies
        // to avoid loading real user settings and playing real audio.
        var peer = _context.Peers.Single();
        var settingsService = new SettingsService(peer.AppDataRoot);
        var appSettings = settingsService.LoadSettings();
        appSettings.BaseFolder = peer.BaseFolder;
        appSettings.P2P = new P2PSettings { Enabled = false };

        var mockAudio = new Mock<IAudioPlaybackService>();

        var appVm = new ApplicationViewModel(
            settingsService,
            audioServiceFactory: () => mockAudio.Object);
        var orchestrator = appVm.SyncOrchestrator;

        Assert.False(appVm.P2PIsConnected);
        Assert.Equal(orchestrator.ConnectedPeerCount, appVm.P2PPeerCount);
    }

    [Fact]
    public void NavigationScenario()
    {
        var peer = _context.Peers.Single();
        var settingsService = new SettingsService(peer.AppDataRoot);
        var appSettings = settingsService.LoadSettings();
        appSettings.BaseFolder = peer.BaseFolder;
        appSettings.P2P = new P2PSettings { Enabled = false };

        var mockAudio = new Mock<IAudioPlaybackService>();

        var appVm = new ApplicationViewModel(
            settingsService,
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
