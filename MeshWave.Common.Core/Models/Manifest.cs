namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a signed operation in the append-only manifest.
/// Operations are: Create, Update, Delete (tombstone).
/// </summary>
public class ManifestOperation
{
    public required string OperationId { get; set; }
    public required ManifestOperationType OperationType { get; set; }
    public required string TargetId { get; set; }
    public required string TargetType { get; set; }
    public string? ContentHash { get; set; }
    public int SequenceNumber { get; set; }
    public required string Signature { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public enum ManifestOperationType
{
    Create,
    Update,
    Delete,
    /// <summary>Records that the user played a track. Rate-capped during manifest merge.</summary>
    Play,
    /// <summary>Records that the local user follows a peer (TargetId = peer UserId).</summary>
    Follow,
    /// <summary>Records that the local user unfollowed a peer (TargetId = peer UserId).</summary>
    Unfollow,
    /// <summary>Broadcasts the user's profile fields (IsArtist, Bio, BannerImageHash, Website, DisplayName).</summary>
    Profile,
    /// <summary>Signed user-authored comment operation on a track (supports ReplyToId threading).</summary>
    Comment,
    /// <summary>Signed soft-delete for a previously authored comment operation.</summary>
    CommentDelete
}

/// <summary>
/// Manifest: append-only list of signed operations per user.
/// </summary>
public class Manifest
{
    public required string UserId { get; set; }
    public List<ManifestOperation> Operations { get; set; } = [];
    public int Version { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
