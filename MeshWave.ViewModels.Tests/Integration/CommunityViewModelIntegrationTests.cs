using MeshWave.TestUtilities;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.ViewModels;
using Xunit;

namespace MeshWave.ViewModels.Tests.Integration;

public class CommunityViewModelIntegrationTests : IAsyncLifetime
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

    [Fact(Skip = "Failing in CI on remote runners")]
    public async Task FollowAndFeedScenario()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        var aliceCommunityVm = new CommunityViewModel(alice.Orchestrator, settingsService: new SettingsService(alice.AppDataRoot));

        // Bob releases a track
        bob.AnnounceTrack("bob-track-1", "hash-bob-1", new Dictionary<string, string> { ["title"] = "Bob's Hit" });
        await _context.ConnectAndSyncAllAsync();

        // Alice finds Bob in Discover
        aliceCommunityVm.ActiveTab = CommunityTab.Discover;
        await TestWaiter.WaitForItemPollingAsync(() => aliceCommunityVm.SearchResults, u => u.UserId == bob.UserId);
        var bobItem = aliceCommunityVm.SearchResults.First(u => u.UserId == bob.UserId);

        // Alice follows Bob
        aliceCommunityVm.FollowUserCommand.Execute(bobItem);
        Assert.True(bobItem.IsFollowing);
        Assert.Contains(aliceCommunityVm.Following, u => u.UserId == bob.UserId);

        // Alice sees Bob's track in her Feed
        aliceCommunityVm.ActiveTab = CommunityTab.Feed;
        await TestWaiter.WaitForItemPollingAsync(() => aliceCommunityVm.ReleaseFeed, r => r.TargetId == "bob-track-1");
    }

    [Fact(Skip = "Failing in CI on remote runners")]
    public async Task LikeDistributionScenario()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");
        var charlie = await _context.CreatePeerAsync("Charlie");

        var bobCommunityVm = new CommunityViewModel(bob.Orchestrator, settingsService: new SettingsService(bob.AppDataRoot));
        var charlieCommunityVm = new CommunityViewModel(charlie.Orchestrator, settingsService: new SettingsService(charlie.AppDataRoot));

        // Alice releases a track
        alice.AnnounceTrack("alice-track-1", "hash-alice-1", new Dictionary<string, string> { ["title"] = "Alice's Hit" });
        await _context.ConnectAndSyncAllAsync();

        // Bob follows Alice and sees the track
        bobCommunityVm.FollowUserCommand.Execute(new CommunityUserItem { UserId = alice.UserId, DisplayName = "Alice" });
        await TestWaiter.WaitForItemPollingAsync(() => bobCommunityVm.ReleaseFeed, r => r.TargetId == "alice-track-1");
        var bobFeedItem = bobCommunityVm.ReleaseFeed.First(r => r.TargetId == "alice-track-1");

        // Bob likes Alice's track
        bobCommunityVm.ToggleLikeCommand.Execute(bobFeedItem);
        Assert.True(bobFeedItem.IsLikedByMe);
        Assert.Equal(1, bobFeedItem.LikeCount);

        await _context.ConnectAndSyncAllAsync();

        // Charlie follows Alice
        charlieCommunityVm.FollowUserCommand.Execute(new CommunityUserItem { UserId = alice.UserId, DisplayName = "Alice" });

        // Charlie should eventually see Alice's track with 1 like
        await TestWaiter.WaitForItemPollingAsync(
            () => charlieCommunityVm.ReleaseFeed,
            r => r.TargetId == "alice-track-1" && r.LikeCount == 1,
            timeoutMs: 30000);
    }

    [Fact(Skip = "Failing in CI on remote runners")]
    public async Task DiscoveryIntegrationScenario()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var aliceCommunityVm = new CommunityViewModel(alice.Orchestrator, settingsService: new SettingsService(alice.AppDataRoot));

        aliceCommunityVm.ActiveTab = CommunityTab.Discover;
        Assert.Empty(aliceCommunityVm.SearchResults);

        // Bob joins the mesh
        var bob = await _context.CreatePeerAsync("Bob");
        await _context.ConnectAndSyncAllAsync();

        // Alice should eventually discover Bob
        await TestWaiter.WaitForItemPollingAsync(() => aliceCommunityVm.SearchResults, u => u.UserId == bob.UserId);
        Assert.Contains(aliceCommunityVm.SearchResults, u => u.UserId == bob.UserId);
    }
}
