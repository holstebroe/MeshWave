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

public enum ManifestStreamType
{
    Content,
    Interaction,
    Social
}

public static class ManifestStreamMapper
{
    public static ManifestStreamType GetStreamType(ManifestOperationType operationType)
    {
        return operationType switch
        {
            ManifestOperationType.Create or
            ManifestOperationType.Update or
            ManifestOperationType.Delete => ManifestStreamType.Content,

            ManifestOperationType.Play or
            ManifestOperationType.Like or
            ManifestOperationType.Unlike or
            ManifestOperationType.Comment or
            ManifestOperationType.CommentDelete => ManifestStreamType.Interaction,

            ManifestOperationType.Follow or
            ManifestOperationType.Unfollow or
            ManifestOperationType.Profile or
            ManifestOperationType.FriendAdd or
            ManifestOperationType.FriendRemove or
            ManifestOperationType.GroupJoin or
            ManifestOperationType.GroupLeave or
            ManifestOperationType.CreateCompetition or
            ManifestOperationType.CompetitionSubmit or
            ManifestOperationType.CompetitionCastVote or
            ManifestOperationType.CompetitionRevealResults or
            ManifestOperationType.CreateChannel or
            ManifestOperationType.PostMessage => ManifestStreamType.Social,

            ManifestOperationType.Unknown => ManifestStreamType.Content,

            _ => ManifestStreamType.Content
        };
    }
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
    CommentDelete,
    /// <summary>Signed social graph operation: add friend relation to another user.</summary>
    FriendAdd,
    /// <summary>Signed social graph operation: remove friend relation from another user.</summary>
    FriendRemove,
    /// <summary>Signed social graph operation: join a group.</summary>
    GroupJoin,
    /// <summary>Signed social graph operation: leave a group.</summary>
    GroupLeave,
    /// <summary>Signed user reaction operation: like a track.</summary>
    Like,
    /// <summary>Signed user reaction operation: remove like from a track.</summary>
    Unlike,
    /// <summary>Signed administrative operation to start a new competition.</summary>
    CreateCompetition,
    /// <summary>Signed member operation to submit a track to a competition.</summary>
    CompetitionSubmit,
    /// <summary>Signed member operation to cast a sealed vote in a competition.</summary>
    CompetitionCastVote,
    /// <summary>Signed administrative operation to reveal and certify competition results.</summary>
    CompetitionRevealResults,
    /// <summary>Signed administrative operation to create a new group channel.</summary>
    CreateChannel,
    /// <summary>Signed user operation to post a message to a group channel.</summary>
    PostMessage,

    /// <summary>A fallback type for unknown or newer operations parsed from older clients.</summary>
    Unknown = 999
}

/// <summary>
/// Represents a squashed state of a manifest up to a certain sequence number.
/// </summary>
public class ManifestSnapshot
{
    /// <summary>The sequence number of the last operation included in this snapshot.</summary>
    public int LastSequenceNumber { get; set; }

    /// <summary>UTC timestamp when the snapshot was created.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>RSA signature of the snapshot state, signed by the user's private key.</summary>
    public required string Signature { get; set; }

    /// <summary>Canonical hash of the entire squashed library state (Set Verification).</summary>
    public string? LibraryStateDigest { get; set; }

    // --- Squashed State ---

    /// <summary>Cumulative play counts: TrackId -> Total Plays.</summary>
    public Dictionary<string, int> PlayCounts { get; set; } = [];

    /// <summary>Current set of followed UserIds.</summary>
    public List<string> FollowedUserIds { get; set; } = [];

    /// <summary>Current set of liked TrackIds.</summary>
    public List<string> LikedTrackIds { get; set; } = [];

    /// <summary>Current set of friend UserIds.</summary>
    public List<string> FriendUserIds { get; set; } = [];

    /// <summary>Current set of joined GroupIds.</summary>
    public List<string> GroupIds { get; set; } = [];

    /// <summary>Latest metadata for entities (Tracks, Albums, Profiles, etc.).</summary>
    public List<SnapshotStateEntry> EntityStates { get; set; } = [];

    /// <summary>Operations that are preserved even when squashed (e.g., Comments).</summary>
    public List<ManifestOperation> PersistentOperations { get; set; } = [];
}

/// <summary>
/// Represents the latest state of an entity in a snapshot.
/// </summary>
public class SnapshotStateEntry
{
    public required string TargetId { get; set; }
    public required string TargetType { get; set; }
    public string? ContentHash { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Manifest: append-only list of signed operations per user, optionally starting from a snapshot.
/// </summary>
public class Manifest
{
    public required string UserId { get; set; }

    public ManifestStreamType StreamType { get; set; } = ManifestStreamType.Content;

    /// <summary>
    /// Optional snapshot representing the state up to a certain sequence number.
    /// If present, <see cref="Operations"/> should only contain operations with SequenceNumber > Snapshot.LastSequenceNumber.
    /// </summary>
    public ManifestSnapshot? Snapshot { get; set; }

    public List<ManifestOperation> Operations { get; set; } = [];
    public int Version { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
