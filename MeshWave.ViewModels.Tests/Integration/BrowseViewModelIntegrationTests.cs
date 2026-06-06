using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using MeshWave.ViewModels;
using Xunit;

namespace MeshWave.ViewModels.Tests.Integration;

public class BrowseViewModelIntegrationTests : IAsyncLifetime
{
    private MeshTestContext _context = null!;

    public ValueTask InitializeAsync()
    {
        _context = new MeshTestContext();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Stress)]
    public async Task BrowsingReleasesTracksWithUpdates()
    {
        var john = await _context.CreatePeerAsync("John");
        var jane = await _context.CreatePeerAsync("Jane");

        var johnBrowseViewModel = new BrowseViewModel(john.Orchestrator, settingsService: new Services.SettingsService(john.AppDataRoot));
        var janeBrowseViewModel = new BrowseViewModel(jane.Orchestrator, settingsService: new Services.SettingsService(jane.AppDataRoot));

        // Verify that john and jane are connected
        await john.WaitForConditionAsync(() => john.Orchestrator.ConnectedPeerCount > 0);
        await jane.WaitForConditionAsync(() => jane.Orchestrator.ConnectedPeerCount > 0);

        // Force a sync to ensure profiles are exchanged immediately
        await john.Orchestrator.SyncAllPeersAsync();
        await jane.Orchestrator.SyncAllPeersAsync();

        // Verify that john can see jane in the browse view (as an artist)
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Artists, a => a.UserId == jane.UserId, timeoutMs: 60000);

        // Verify that john cannot see any released tracks from jane and vice versa
        Assert.DoesNotContain(jane.UserId, johnBrowseViewModel.Tracks.Select(t => t.ArtistUserId));
        Assert.DoesNotContain(john.UserId, janeBrowseViewModel.Tracks.Select(t => t.ArtistUserId));

        // Action: John releases two tracks
        john.AnnounceTrack("john-track-1", "hash-1", new Dictionary<string, string> { ["title"] = "John's First" });
        john.AnnounceTrack("john-track-2", "hash-2", new Dictionary<string, string> { ["title"] = "John's Second" });

        // Action: Jane releases one track
        jane.AnnounceTrack("jane-track-1", "hash-jane-1", new Dictionary<string, string> { ["title"] = "Jane's First" });

        await _context.ConnectAndSyncAllAsync();

        // Extra sync to be absolutely sure after actions
        await john.SyncAsync();
        await jane.SyncAsync();

        // Verify that john can see jane's track and vice versa
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "jane-track-1", timeoutMs: 60000);
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => janeBrowseViewModel.Tracks, t => t.TrackId == "john-track-1", timeoutMs: 60000);
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => janeBrowseViewModel.Tracks, t => t.TrackId == "john-track-2", timeoutMs: 60000);

        // Action: Jane releases 2 more tracks
        jane.AnnounceTrack("jane-track-2", "hash-jane-2", new Dictionary<string, string> { ["title"] = "Jane's Second" });
        jane.AnnounceTrack("jane-track-3", "hash-jane-3", new Dictionary<string, string> { ["title"] = "Jane's Third" });

        await _context.ConnectAndSyncAllAsync();

        // Verify that john can see all of jane's released tracks
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "jane-track-2", timeoutMs: 60000);
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "jane-track-3", timeoutMs: 60000);
        Assert.Equal(3, johnBrowseViewModel.Tracks.Count(t => t.ArtistUserId == jane.UserId));

        // Action: John un-releases one of his tracks (deletes it from manifest)
        var johnContentManifest = john.GetLocalManifest(ManifestStreamType.Content);
        var mm = new ManifestManager();
        mm.AppendSignedOperation(johnContentManifest!, ManifestOperationType.Delete, "john-track-2", "Track", null, null, john.Identity.PrivateKeyPem);

        john.Orchestrator.SaveLocalManifests();
        await john.Orchestrator.CatalogueService.IngestAsync(johnContentManifest!);

        // Force a sync
        await john.SyncAsync();

        await _context.ConnectAndSyncAllAsync();

        // Verify that jane can no longer see the un-released track
        await jane.WaitForConditionAsync(() => !janeBrowseViewModel.Tracks.Any(t => t.TrackId == "john-track-2"));
    }

    [Fact]
    public async Task SearchFilteringIntegration()
    {
        var john = await _context.CreatePeerAsync("John");
        var jane = await _context.CreatePeerAsync("Jane");

        var johnBrowseViewModel = new BrowseViewModel(john.Orchestrator, settingsService: new Services.SettingsService(john.AppDataRoot));

        john.AnnounceTrack("track-rock", "hash-rock", new Dictionary<string, string> { ["title"] = "Rock Song", ["album"] = "Rock Album" });
        jane.AnnounceTrack("track-pop", "hash-pop", new Dictionary<string, string> { ["title"] = "Pop Song", ["album"] = "Pop Album" });

        await _context.ConnectAndSyncAllAsync();

        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "track-pop", timeoutMs: 60000);

        johnBrowseViewModel.FilterText = "Rock";
        Assert.Contains(johnBrowseViewModel.Tracks, t => t.Title.Contains("Rock"));
        Assert.DoesNotContain(johnBrowseViewModel.Tracks, t => t.Title.Contains("Pop"));

        johnBrowseViewModel.FilterText = "Pop";
        Assert.Contains(johnBrowseViewModel.Tracks, t => t.Title.Contains("Pop"));
        Assert.DoesNotContain(johnBrowseViewModel.Tracks, t => t.Title.Contains("Rock"));
    }

    [Fact]
    public async Task DownloadLifecycleIntegration()
    {
        var trackId = "john-track-1";
        var hash = "abc-123-hash";
        byte[] content = [1, 2, 3, 4];

        var john = await _context.CreatePeerAsync("John", testDataName: "John", contentProvider: h => h == hash ? content : null);
        var jane = await _context.CreatePeerAsync("Jane");

        var janeBrowseViewModel = new BrowseViewModel(jane.Orchestrator, settingsService: new Services.SettingsService(jane.AppDataRoot));

        john.AnnounceTrack(trackId, hash, new Dictionary<string, string> { ["title"] = "Downloadable Track" });

        await _context.ConnectAndSyncAllAsync();

        await ViewModelTestHelpers.WaitForItemPollingAsync(() => janeBrowseViewModel.Tracks, t => t.TrackId == trackId, timeoutMs: 60000);
        var trackItem = janeBrowseViewModel.Tracks.First(t => t.TrackId == trackId);

        Assert.True(trackItem.CanDownload);

        // Action: Jane starts download
        janeBrowseViewModel.DownloadTrackCommand.Execute(trackItem);

        // Verify it enters queued/downloading state
        await jane.WaitForConditionAsync(() => trackItem.IsQueued || trackItem.IsDownloaded);

        // Verify completion
        await jane.WaitForConditionAsync(() => trackItem.IsDownloaded, timeoutMs: 15000);
        Assert.False(trackItem.IsQueued);
        Assert.True(trackItem.IsDownloaded);
        Assert.False(trackItem.CanDownload);
    }
}
