using System.Linq;
using System.Threading.Tasks;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class GroupSyncTests
{
    [Fact]
    public async Task RecordCreateChannel_SyncsAcrossPeers()
    {
        await using var ctx = new MeshTestContext();
        var peer1 = await ctx.CreatePeerAsync("peer1");
        var peer2 = await ctx.CreatePeerAsync("peer2");

        GroupStateChangedEventArgs? stateChange = null;
        peer2.Orchestrator.GroupStateChanged += (sender, args) => stateChange = args;

        peer1.Orchestrator.RecordCreateChannel("chan-1", "group-1", "General Chat");

        await peer1.SyncAsync();
        await peer2.SyncAsync();
        await ctx.ConnectAndSyncAllAsync();

        var p1SocialManifest = peer2.GetPeerManifest(peer1.UserId, ManifestStreamType.Social);
        Assert.NotNull(p1SocialManifest);
        var op = p1SocialManifest.Operations.FirstOrDefault(o => o.OperationType == ManifestOperationType.CreateChannel);
        Assert.NotNull(op);
        Assert.Equal("chan-1", op.TargetId);
        Assert.Equal("GroupChannel", op.TargetType);
        Assert.Equal("group-1", op.Metadata["groupId"]);
        Assert.Equal("General Chat", op.Metadata["name"]);

        Assert.NotNull(stateChange);
        Assert.Equal(ManifestOperationType.CreateChannel, stateChange.OperationType);
        Assert.Equal("chan-1", stateChange.TargetId);
        Assert.Equal("group-1", stateChange.Metadata["groupId"]);
    }

    [Fact]
    public async Task RecordPostMessage_SyncsAcrossPeers()
    {
        await using var ctx = new MeshTestContext();
        var peer1 = await ctx.CreatePeerAsync("peer1");
        var peer2 = await ctx.CreatePeerAsync("peer2");

        GroupMessageEventArgs? messageReceived = null;
        peer2.Orchestrator.GroupMessageReceived += (sender, args) => messageReceived = args;

        peer1.Orchestrator.RecordPostMessage("post-1", "chan-1", "Hello World!", "parent-post-id");

        await peer1.SyncAsync();
        await peer2.SyncAsync();
        await ctx.ConnectAndSyncAllAsync();

        var p1SocialManifest = peer2.GetPeerManifest(peer1.UserId, ManifestStreamType.Social);
        Assert.NotNull(p1SocialManifest);
        var op = p1SocialManifest.Operations.FirstOrDefault(o => o.OperationType == ManifestOperationType.PostMessage);
        Assert.NotNull(op);
        Assert.Equal("post-1", op.TargetId);
        Assert.Equal("GroupChannel", op.TargetType);
        Assert.Equal("chan-1", op.Metadata["channelId"]);
        Assert.Equal("Hello World!", op.Metadata["content"]);
        Assert.Equal("parent-post-id", op.Metadata["parentPostId"]);

        Assert.NotNull(messageReceived);
        Assert.Equal("chan-1", messageReceived.ChannelId);
        Assert.Equal("post-1", messageReceived.PostId);
        Assert.Equal("Hello World!", messageReceived.Content);
        Assert.Equal("parent-post-id", messageReceived.ParentPostId);
    }
}
