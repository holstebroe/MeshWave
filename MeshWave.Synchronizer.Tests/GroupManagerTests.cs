using System;
using System.Linq;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Serialization;
using MeshWave.Synchronizer.Groups;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class GroupManagerTests
{
    [Fact]
    public void MergeGroupManifest_ShouldIndexPostsByChannelAndParent()
    {
        var manager = new GroupManager();

        var post1 = new PostMessage
        {
            PostId = "post1",
            ChannelId = "chan1",
            AuthorUserId = "user1",
            Content = "Hello",
            Signature = "sig"
        };

        var post2 = new PostMessage
        {
            PostId = "post2",
            ChannelId = "chan1",
            ParentPostId = "post1",
            AuthorUserId = "user2",
            Content = "Hi there",
            Signature = "sig"
        };

        var manifest = new GroupManifest
        {
            GroupId = "group1",
            Name = "Group 1",
            FounderUserId = "user1",
            Operations = new System.Collections.Generic.List<GroupOperation>
            {
                new GroupOperation
                {
                    SequenceNumber = 1,
                    UserId = "user1",
                    OperationType = GroupOperationType.Post,
                    Signature = "sig1",
                    Metadata = { { "PostMessageJson", JsonSerializer.SerializePostMessage(post1) } }
                },
                new GroupOperation
                {
                    SequenceNumber = 2,
                    UserId = "user2",
                    OperationType = GroupOperationType.Post,
                    Signature = "sig2",
                    Metadata = { { "PostMessageJson", JsonSerializer.SerializePostMessage(post2) } }
                }
            }
        };

        manager.MergeGroupManifest(manifest);

        var chan1Messages = manager.GetMessagesForChannel("chan1");
        Assert.Equal(2, chan1Messages.Count);

        var replies = manager.GetRepliesForPost("post1");
        Assert.Single(replies);
        Assert.Equal("post2", replies[0].PostId);
    }

    [Fact]
    public void MergeGroupManifest_ShouldHandleSoftDeletes()
    {
        var manager = new GroupManager();

        var post1 = new PostMessage
        {
            PostId = "post1",
            ChannelId = "chan1",
            AuthorUserId = "user1",
            Content = "Bad message",
            Signature = "sig"
        };

        var manifest = new GroupManifest
        {
            GroupId = "group1",
            Name = "Group 1",
            FounderUserId = "user1",
            Operations = new System.Collections.Generic.List<GroupOperation>
            {
                new GroupOperation
                {
                    SequenceNumber = 1,
                    UserId = "user1",
                    OperationType = GroupOperationType.Post,
                    Signature = "sig1",
                    Metadata = { { "PostMessageJson", JsonSerializer.SerializePostMessage(post1) } }
                },
                new GroupOperation
                {
                    SequenceNumber = 2,
                    UserId = "admin1",
                    OperationType = GroupOperationType.Moderate,
                    Signature = "sig2",
                    Metadata =
                    {
                        { "Action", "DeletePost" },
                        { "TargetPostId", "post1" }
                    }
                }
            }
        };

        manager.MergeGroupManifest(manifest);

        var chan1Messages = manager.GetMessagesForChannel("chan1");
        Assert.Empty(chan1Messages);

        Assert.Null(manager.GetPost("post1"));
        Assert.True(manager.IsPostDeleted("post1"));
    }
}
