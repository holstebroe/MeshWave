using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using MeshWave.ViewModels;
using Xunit;

namespace MeshWave.ViewModels.Tests;

public class CommunityViewModelIntegrationTests : IAsyncLifetime
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
    public async Task FollowAndFeedScenario()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        var aliceCommunityVm = new CommunityViewModel(alice.Orchestrator);

        // Bob releases a track
        bob.AnnounceTrack("bob-track-1", "hash-bob-1", new Dictionary<string, string> { ["title"] = "Bob's Hit" });
        await _context.ConnectAndSyncAllAsync();

        // Alice finds Bob in Discover
        aliceCommunityVm.ActiveTab = CommunityTab.Discover;
        await aliceCommunityVm.SearchResults.WaitForItemAsync(u => u.UserId == bob.UserId);
        var bobItem = aliceCommunityVm.SearchResults.First(u => u.UserId == bob.UserId);

        // Alice follows Bob
        aliceCommunityVm.FollowUserCommand.Execute(bobItem);
        Assert.True(bobItem.IsFollowing);
        Assert.Contains(aliceCommunityVm.Following, u => u.UserId == bob.UserId);

        // Alice sees Bob's track in her Feed
        aliceCommunityVm.ActiveTab = CommunityTab.Feed;
        await aliceCommunityVm.ReleaseFeed.WaitForItemAsync(r => r.TargetId == "bob-track-1");
    }

    [Fact]
    public async Task LikeDistributionScenario()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");

        var aliceCommunityVm = new CommunityViewModel(alice.Orchestrator);
        var bobCommunityVm = new CommunityViewModel(bob.Orchestrator);

        // Alice releases a track
        alice.AnnounceTrack("alice-track-1", "hash-alice-1", new Dictionary<string, string> { ["title"] = "Alice's Hit" });
        await _context.ConnectAndSyncAllAsync();

        // Bob follows Alice and sees the track
        var aliceItem = new CommunityUserItem { UserId = alice.UserId, DisplayName = "Alice" };
        bobCommunityVm.FollowUserCommand.Execute(aliceItem);
        await bobCommunityVm.ReleaseFeed.WaitForItemAsync(r => r.TargetId == "alice-track-1");
        var feedItem = bobCommunityVm.ReleaseFeed.First(r => r.TargetId == "alice-track-1");

        // Bob likes Alice's track
        bobCommunityVm.ToggleLikeCommand.Execute(feedItem);
        Assert.True(feedItem.IsLikedByMe);
        Assert.Equal(1, feedItem.LikeCount);

        await bob.SyncAsync();
        await _context.ConnectAndSyncAllAsync();

        // Alice follows Bob (so she can see his interactions in some view, though Feed is for followed users' CREATIONS)
        // Wait, Likes are in Interaction stream. CommunityViewModel.RefreshFeed() only loads from followed users.
        // Actually, RefreshFeed loads releases from followed users, and then it re-builds likes index from ALL peer manifests.

        aliceCommunityVm.ActiveTab = CommunityTab.Following; // Just to trigger something
        // Alice should see the like count on her own track if she follows someone who liked it?
        // No, RebuildLikesIndex scans ALL peer manifests.

        // Alice follows Bob to be sure she has his manifest
        var bobItem = new CommunityUserItem { UserId = bob.UserId, DisplayName = "Bob" };
        aliceCommunityVm.FollowUserCommand.Execute(bobItem);

        await aliceCommunityVm.ReleaseFeed.WaitForItemAsync(r => r.TargetId == "alice-track-1");
        var aliceHit = aliceCommunityVm.ReleaseFeed.First(r => r.TargetId == "alice-track-1");

        await alice.WaitForConditionAsync(() => aliceHit.LikeCount == 1);
        Assert.Equal(1, aliceHit.LikeCount);
    }

    [Fact]
    public async Task DiscoveryIntegrationScenario()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var aliceCommunityVm = new CommunityViewModel(alice.Orchestrator);

        aliceCommunityVm.ActiveTab = CommunityTab.Discover;
        Assert.Empty(aliceCommunityVm.SearchResults);

        // Bob joins the mesh
        var bob = await _context.CreatePeerAsync("Bob");
        await _context.ConnectAndSyncAllAsync();

        // Alice should eventually discover Bob
        await aliceCommunityVm.SearchResults.WaitForItemAsync(u => u.UserId == bob.UserId);
        Assert.Contains(aliceCommunityVm.SearchResults, u => u.UserId == bob.UserId);
    }
}
