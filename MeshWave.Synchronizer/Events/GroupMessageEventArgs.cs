using System;

namespace MeshWave.Synchronizer;

public class GroupMessageEventArgs(string userId, string channelId, string postId, string content, string? parentPostId = null) : EventArgs
{
    public string UserId { get; } = userId;
    public string ChannelId { get; } = channelId;
    public string PostId { get; } = postId;
    public string Content { get; } = content;
    public string? ParentPostId { get; } = parentPostId;
}
