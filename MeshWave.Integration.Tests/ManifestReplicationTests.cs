using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using Xunit;

namespace MeshWave.Integration.Tests;

/// <summary>
/// Manifest replication at scale: operations travel as deltas, every peer stores and forwards the signed streams it
/// holds, and anti-entropy only compares heads.
/// </summary>
public class ManifestReplicationTests : IAsyncLifetime
{
    private MeshTestContext _context = default!;

    public ValueTask InitializeAsync()
    {
        _context = new MeshTestContext();
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private static int CountComments(TestPeer peer, string authorUserId)
    {
        var manifest = peer.GetPeerManifest(authorUserId, ManifestStreamType.Interaction);
        if (manifest == null) return 0;
        lock (manifest)
            return manifest.AllOperations().Count(op => op.OperationType == ManifestOperationType.Comment);
    }

    private static async Task WaitAsync(Func<bool> condition, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static PeerInfo Endpoint(TestPeer peer)
    {
        return new PeerInfo { UserId = peer.UserId, DisplayName = peer.Name, Address = "127.0.0.1", Port = peer.Port, PublicKeyPem = peer.Identity.PublicKeyPem };
    }

    [Fact]
    public async Task Like_IsPushedAsADeltaOfOneOperation_NotTheWholeStream()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");
        for (var i = 0; i < 20; i++) alice.CommentOn("track-1", $"comment {i}");
        await _context.ConnectAndSyncAllAsync();
        await WaitAsync(() => CountComments(bob, alice.UserId) == 20);

        alice.Like("track-1");

        await WaitAsync(() => bob.HasOperation(alice.UserId, ManifestStreamType.Interaction, op => op.OperationType == ManifestOperationType.Like));
        var pushesFromAlice = bob.Orchestrator.GetPeerDiagnosticsSnapshots()
            .Single(p => p.UserId == alice.UserId).RecentMessages
            .Where(m => m.MessageType == "PushManifest" && m.Details.Contains("Received Interaction delta"))
            .ToList();
        Assert.Contains(pushesFromAlice, m => m.Details.Contains("with 1 operation(s)"));
        Assert.DoesNotContain(pushesFromAlice, m => m.Details.Contains("with 21 operation(s)"));
    }

    [Fact]
    public async Task Peer_ServesTheSignedStreamsOfOtherAuthors_AndTheirHeads()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");
        alice.CommentOn("track-1", "hello from alice");
        await _context.ConnectAndSyncAllAsync();
        await WaitAsync(() => CountComments(bob, alice.UserId) == 1);

        var client = new ManifestExchangeClient();
        var aliceSocial = ManifestManager.GetHead(alice.GetLocalManifest(ManifestStreamType.Social)!);

        // Bob reports Alice's stream heads...
        var heads = await client.FetchHeadsAsync(Endpoint(bob), TestContext.Current.CancellationToken);
        Assert.NotNull(heads);
        Assert.Contains(aliceSocial, heads);

        // ...and serves Alice's stream from the start, with her key, verifiable against her UserId.
        var stream = await client.FetchStreamAsync(Endpoint(bob), alice.UserId, ManifestStreamType.Interaction, 0, TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.Equal(alice.UserId, stream.UserId);
        Assert.Equal(alice.Identity.PublicKeyPem, stream.AuthorPublicKey);
        Assert.True(new ManifestManager().VerifyManifest(stream, stream.AuthorPublicKey!));
        Assert.Contains(stream.Operations, op => op.Metadata.GetValueOrDefault("text") == "hello from alice");
    }

    [Fact]
    public async Task OfflineAuthorsComments_ReachALateJoiner_ThroughAnotherPeer()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");
        alice.CommentOn("track-1", "written while carol was away");
        await _context.ConnectAndSyncAllAsync();
        await WaitAsync(() => CountComments(bob, alice.UserId) == 1);

        await alice.Orchestrator.StopAsync();

        var carol = await _context.CreatePeerAsync("Carol");
        await carol.WaitForConditionAsync(() => CountComments(carol, alice.UserId) == 1, timeoutMs: 30000);

        var comment = carol.GetPeerManifest(alice.UserId, ManifestStreamType.Interaction)!.AllOperations()
            .Single(op => op.OperationType == ManifestOperationType.Comment);
        Assert.Equal("written while carol was away", comment.Metadata["text"]);
    }

    [Fact]
    public async Task PeerThatLostItsLocalData_RecoversItsOwnStream_InsteadOfForkingIt()
    {
        var alice = await _context.CreatePeerAsync("Alice");
        var bob = await _context.CreatePeerAsync("Bob");
        for (var i = 0; i < 3; i++) alice.CommentOn("track-1", $"comment {i}");
        await _context.ConnectAndSyncAllAsync();
        await WaitAsync(() => CountComments(bob, alice.UserId) == 3);

        // Alice restarts with the same identity but empty streams, as after losing her local data.
        await alice.Orchestrator.StopAsync();
        var empty = Enum.GetValues<ManifestStreamType>()
            .Select(streamType => new Manifest { UserId = alice.UserId, StreamType = streamType })
            .ToList();
        await alice.StartAsync(initialManifests: empty, bootstrapNodes: [$"127.0.0.1:{_context.BootstrapPort}"]);

        await alice.WaitForConditionAsync(() => ManifestManager.GetHeadSequenceNumber(alice.GetLocalManifest(ManifestStreamType.Interaction)) == 2, timeoutMs: 30000);

        // Her next comment continues the stream, so peers accept it.
        alice.CommentOn("track-1", "after recovery");
        await WaitAsync(() => CountComments(bob, alice.UserId) == 4);
    }
}
