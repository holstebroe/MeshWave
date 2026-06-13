namespace MeshWave.Common.Core.Models;

/// <summary>
/// Defines the types of operations that can occur within a group.
/// </summary>
public enum GroupOperationType
{
    /// <summary>The group was created/founded.</summary>
    Found,
    /// <summary>A user joined the group.</summary>
    Join,
    /// <summary>A user posted a message or content to a channel.</summary>
    Post,
    /// <summary>An administrative or moderation action (e.g., kick, ban, delete post).</summary>
    Moderate
}

/// <summary>
/// Represents a chat or content channel within a group.
/// </summary>
public class GroupChannel
{
    public required string ChannelId { get; set; }
    public required string GroupId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CreatorUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a message posted in a group channel.
/// </summary>
public class PostMessage
{
    public required string PostId { get; set; }
    public required string ChannelId { get; set; }
    public required string AuthorUserId { get; set; }
    public required string Content { get; set; }
    public string? ParentPostId { get; set; }
    public string? AttachmentHash { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Signature { get; set; }
}

/// <summary>
/// Represents a single state-changing event within a group manifest.
/// </summary>
public class GroupOperation
{
    public int SequenceNumber { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string UserId { get; set; }
    public required GroupOperationType OperationType { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    public required string Signature { get; set; }
}

/// <summary>
/// Represents the append-only log of operations for a community-owned Group.
/// </summary>
public class GroupManifest
{
    /// <summary>Stable identifier for the group (e.g., SHA-256 hash of the founding operation).</summary>
    public required string GroupId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string FounderUserId { get; set; }
    public bool IsPublic { get; set; }
    public string? CoverImageHash { get; set; }
    public List<GroupOperation> Operations { get; set; } = [];
}
