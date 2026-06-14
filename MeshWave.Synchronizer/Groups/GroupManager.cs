using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Serialization;

namespace MeshWave.Synchronizer.Groups;

/// <summary>
/// Manages the state and logic for group manifests, including threaded messaging.
/// </summary>
public class GroupManager
{
    private readonly ConcurrentDictionary<string, GroupManifest> _groupManifests = new();

    // Indexes for efficient messaging lookups
    // ChannelId -> Thread-safe list of PostMessages
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PostMessage>> _channelMessages = new();

    // PostId -> PostMessage for O(1) lookup
    private readonly ConcurrentDictionary<string, PostMessage> _allMessages = new();

    // ParentPostId -> Thread-safe list of reply PostMessages
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PostMessage>> _repliesByParent = new();

    // PostId -> IsDeleted (for moderation soft-deletes)
    private readonly ConcurrentDictionary<string, bool> _deletedMessages = new();

    public GroupManager()
    {
    }

    /// <summary>
    /// Processes a new GroupManifest or a merge update.
    /// Reconstructs the internal threaded messaging state.
    /// </summary>
    public void MergeGroupManifest(GroupManifest manifest)
    {
        _groupManifests[manifest.GroupId] = manifest;

        foreach (var op in manifest.Operations.OrderBy(o => o.SequenceNumber))
        {
            ApplyOperation(manifest.GroupId, op);
        }
    }

    private void ApplyOperation(string groupId, GroupOperation op)
    {
        if (op.OperationType == GroupOperationType.Post)
        {
            if (op.Metadata.TryGetValue("PostMessageJson", out string? json))
            {
                var post = JsonSerializer.DeserializePostMessage(json);
                if (post != null && post.ChannelId != null)
                {
                    _allMessages[post.PostId] = post;

                    var channelSet = _channelMessages.GetOrAdd(post.ChannelId, _ => new ConcurrentDictionary<string, PostMessage>());
                    channelSet.TryAdd(post.PostId, post);

                    if (!string.IsNullOrEmpty(post.ParentPostId))
                    {
                        var repliesSet = _repliesByParent.GetOrAdd(post.ParentPostId, _ => new ConcurrentDictionary<string, PostMessage>());
                        repliesSet.TryAdd(post.PostId, post);
                    }
                }
            }
        }
        else if (op.OperationType == GroupOperationType.Moderate)
        {
            // Soft delete
            if (op.Metadata.TryGetValue("Action", out string? action) && action == "DeletePost")
            {
                if (op.Metadata.TryGetValue("TargetPostId", out string? targetPostId))
                {
                    _deletedMessages[targetPostId] = true;
                }
            }
        }
    }

    /// <summary>
    /// Gets all active (non-deleted) messages for a specific channel, optionally ordered by timestamp.
    /// </summary>
    public IReadOnlyList<PostMessage> GetMessagesForChannel(string channelId)
    {
        if (_channelMessages.TryGetValue(channelId, out var messages))
        {
            // taking a snapshot of values from ConcurrentDictionary is thread-safe
            return messages.Values.Where(m => !_deletedMessages.ContainsKey(m.PostId))
                           .OrderBy(m => m.Timestamp)
                           .ToList();
        }
        return Array.Empty<PostMessage>();
    }

    /// <summary>
    /// Gets all active (non-deleted) replies to a specific parent post.
    /// </summary>
    public IReadOnlyList<PostMessage> GetRepliesForPost(string parentPostId)
    {
        if (_repliesByParent.TryGetValue(parentPostId, out var replies))
        {
            return replies.Values.Where(m => !_deletedMessages.ContainsKey(m.PostId))
                          .OrderBy(m => m.Timestamp)
                          .ToList();
        }
        return Array.Empty<PostMessage>();
    }

    /// <summary>
    /// Gets a specific post by ID, returning null if it doesn't exist or is deleted.
    /// </summary>
    public PostMessage? GetPost(string postId)
    {
        if (_deletedMessages.ContainsKey(postId))
            return null;

        _allMessages.TryGetValue(postId, out var post);
        return post;
    }

    /// <summary>
    /// For testing/diagnostics: returns if a post was soft-deleted.
    /// </summary>
    public bool IsPostDeleted(string postId)
    {
        return _deletedMessages.ContainsKey(postId);
    }
}
