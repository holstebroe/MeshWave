using System.Diagnostics;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.ViewModels;
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

    [Fact(Skip = "TODO: This test fails in ConnectAndSyncAllAsync(); Make this work.")]
    public async Task BrowsingReleasesTracksWithUpdates()
    {
        var john = await _context.CreatePeerAsync("John");
        var jane = await _context.CreatePeerAsync("Jane");

        var johnBrowseViewModel = new BrowseViewModel(john.Orchestrator, settingsService: new SettingsService(john.AppDataRoot));
        var janeBrowseViewModel = new BrowseViewModel(jane.Orchestrator, settingsService: new SettingsService(jane.AppDataRoot));

        // Verify that john and jane are connected
        await john.WaitForConditionAsync(() => john.Orchestrator.ConnectedPeerCount > 0);
        await jane.WaitForConditionAsync(() => jane.Orchestrator.ConnectedPeerCount > 0);

        // Use the built-in ConnectAndSyncAll which properly propagates manifests
        await _context.ConnectAndSyncAllAsync();

        // Trigger refresh of browse viewmodels to load the manifests that were just synced
        // (FilterText change causes Refresh to be called)
        johnBrowseViewModel.FilterText = " ";
        johnBrowseViewModel.FilterText = string.Empty;
        janeBrowseViewModel.FilterText = " ";
        janeBrowseViewModel.FilterText = string.Empty;

        // Give the ViewModels a moment to process the refresh
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Now verify that John can see Jane in browse view
        // Are we sure that Jane is actually reporting herself as artist?
        try
        {
            await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Artists, a => a.UserId == jane.UserId, timeoutMs: 60000);
        }
        catch (Exception ex)
        {
            // Log detailed info about what manifests John has
            var johnManifests = john.Orchestrator.PeerManifests.ToList();
            var johnLocalManifest = john.Orchestrator.GetLocalManifest(ManifestStreamType.Social);
            var janeManifestContent = johnManifests.FirstOrDefault(m => m.StreamType == ManifestStreamType.Content && m.UserId == jane.UserId);
            var janeManifestSocial = johnManifests.FirstOrDefault(m => m.StreamType == ManifestStreamType.Social && m.UserId == jane.UserId);

            var manifesDebugInfo = $"\nJohn's Local Social Manifest: {(johnLocalManifest != null ? "YES" : "NO")}\n" +
                $"John's Peer Manifests: {johnManifests.Count}\n" +
                $"  Social: {johnManifests.Count(m => m.StreamType == ManifestStreamType.Social)}\n" +
                $"  Content: {johnManifests.Count(m => m.StreamType == ManifestStreamType.Content)}\n" +
                $"  Artists in BrowseViewModel: {johnBrowseViewModel.Artists.Count}\n" +
                $"Jane's Social Manifest Details:\n" +
                $"  Found: {(janeManifestSocial != null ? "YES" : "NO")}\n" +
                $"  Operations Count: {(janeManifestSocial?.Operations.Count ?? 0)}\n" +
                $"  All Operations: {(janeManifestSocial != null ? string.Join(",", janeManifestSocial.Operations.Select(o => $"{o.OperationType}({o.SequenceNumber})")) : "NONE")}\n" +
                $"  Snapshot: {(janeManifestSocial?.Snapshot != null ? $"LastSeqNum={janeManifestSocial.Snapshot.LastSequenceNumber}" : "NONE")}\n";

            OutputPeerLogs(john, jane);
            throw new Exception($"{ex.Message}{manifesDebugInfo}\n\n=== JOHN'S LOGS ===\n{john.GetLogsAsString()}\n\n=== JANE'S LOGS ===\n{jane.GetLogsAsString()}", ex);
        }

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
        try
        {
            await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "jane-track-1", timeoutMs: 30000);
        }
        catch (Exception ex)
        {
            OutputPeerLogs(john, jane);
            throw new Exception($"{ex.Message}\n\n=== JOHN'S LOGS ===\n{john.GetLogsAsString()}\n\n=== JANE'S LOGS ===\n{jane.GetLogsAsString()}", ex);
        }

        try
        {
            await ViewModelTestHelpers.WaitForItemPollingAsync(() => janeBrowseViewModel.Tracks, t => t.TrackId == "john-track-1", timeoutMs: 30000);
        }
        catch (Exception ex)
        {
            OutputPeerLogs(john, jane);
            throw new Exception($"{ex.Message}\n\n=== JOHN'S LOGS ===\n{john.GetLogsAsString()}\n\n=== JANE'S LOGS ===\n{jane.GetLogsAsString()}", ex);
        }

        try
        {
            await ViewModelTestHelpers.WaitForItemPollingAsync(() => janeBrowseViewModel.Tracks, t => t.TrackId == "john-track-2", timeoutMs: 30000);
        }
        catch (Exception ex)
        {
            OutputPeerLogs(john, jane);
            throw new Exception($"{ex.Message}\n\n=== JOHN'S LOGS ===\n{john.GetLogsAsString()}\n\n=== JANE'S LOGS ===\n{jane.GetLogsAsString()}", ex);
        }

        // Action: Jane releases 2 more tracks
        jane.AnnounceTrack("jane-track-2", "hash-jane-2", new Dictionary<string, string> { ["title"] = "Jane's Second" });
        jane.AnnounceTrack("jane-track-3", "hash-jane-3", new Dictionary<string, string> { ["title"] = "Jane's Third" });

        await _context.ConnectAndSyncAllAsync();

        // Verify that john can see all of jane's released tracks
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "jane-track-2", timeoutMs: 30000);
        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "jane-track-3", timeoutMs: 30000);
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

        var johnBrowseViewModel = new BrowseViewModel(john.Orchestrator, settingsService: new SettingsService(john.AppDataRoot));

        john.AnnounceTrack("track-rock", "hash-rock", new Dictionary<string, string> { ["title"] = "Rock Song", ["album"] = "Rock Album" });
        jane.AnnounceTrack("track-pop", "hash-pop", new Dictionary<string, string> { ["title"] = "Pop Song", ["album"] = "Pop Album" });

        await _context.ConnectAndSyncAllAsync();

        await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Tracks, t => t.TrackId == "track-pop", timeoutMs: 30000);

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

        var janeBrowseViewModel = new BrowseViewModel(jane.Orchestrator, settingsService: new SettingsService(jane.AppDataRoot));

        john.AnnounceTrack(trackId, hash, new Dictionary<string, string> { ["title"] = "Downloadable Track" });

        await _context.ConnectAndSyncAllAsync();

        await ViewModelTestHelpers.WaitForItemPollingAsync(() => janeBrowseViewModel.Tracks, t => t.TrackId == trackId, timeoutMs: 30000);
        var trackItem = janeBrowseViewModel.Tracks.First(t => t.TrackId == trackId);

        Assert.True(trackItem.CanDownload);

        // Action: Jane starts download
        janeBrowseViewModel.DownloadTrackCommand.Execute(trackItem);

        // Verify it enters queued/downloading state
        await jane.WaitForConditionAsync(() => trackItem.IsQueued || trackItem.IsDownloaded);

        // Verify completion
        await jane.WaitForConditionAsync(() => trackItem.IsDownloaded, timeoutMs: 60000);
        Assert.False(trackItem.IsQueued);
        Assert.True(trackItem.IsDownloaded);
        Assert.False(trackItem.CanDownload);
    }

    private void OutputPeerLogs(TestPeer john, TestPeer jane)
    {
        Debug.WriteLine("=== JOHN'S LOGS ===");
        Debug.WriteLine(john.GetLogsAsString());
        Debug.WriteLine("\n=== JANE'S LOGS ===");
        Debug.WriteLine(jane.GetLogsAsString());

        // Also output to console for visibility in test output
        Console.WriteLine("=== JOHN'S LOGS ===");
        Console.WriteLine(john.GetLogsAsString());
        Console.WriteLine("\n=== JANE'S LOGS ===");
        Console.WriteLine(jane.GetLogsAsString());
    }
}
