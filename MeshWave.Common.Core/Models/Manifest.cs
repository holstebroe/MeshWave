namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a signed operation in the append-only manifest.
/// Operations are: Create, Update, Delete (tombstone).
/// </summary>
public class ManifestOperation
{
    public required string OperationId { get; set; }
    public required string OperationType { get; set; }
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
    private static readonly Dictionary<string, ManifestStreamType> _registry = new(StringComparer.OrdinalIgnoreCase);

    static ManifestStreamMapper()
    {
        // Register Content stream types
        Register(ManifestOperationType.Create, ManifestStreamType.Content);
        Register(ManifestOperationType.Update, ManifestStreamType.Content);
        Register(ManifestOperationType.Delete, ManifestStreamType.Content);

        // Register Interaction stream types
        Register(ManifestOperationType.Play, ManifestStreamType.Interaction);
        Register(ManifestOperationType.Like, ManifestStreamType.Interaction);
        Register(ManifestOperationType.Unlike, ManifestStreamType.Interaction);
        Register(ManifestOperationType.Comment, ManifestStreamType.Interaction);
        Register(ManifestOperationType.CommentDelete, ManifestStreamType.Interaction);

        // Register Social stream types
        Register(ManifestOperationType.Follow, ManifestStreamType.Social);
        Register(ManifestOperationType.Unfollow, ManifestStreamType.Social);
        Register(ManifestOperationType.Profile, ManifestStreamType.Social);
        Register(ManifestOperationType.FriendAdd, ManifestStreamType.Social);
        Register(ManifestOperationType.FriendRemove, ManifestStreamType.Social);
        Register(ManifestOperationType.GroupJoin, ManifestStreamType.Social);
        Register(ManifestOperationType.GroupLeave, ManifestStreamType.Social);
        Register(ManifestOperationType.CreateCompetition, ManifestStreamType.Social);
        Register(ManifestOperationType.CompetitionSubmit, ManifestStreamType.Social);
        Register(ManifestOperationType.CompetitionCastVote, ManifestStreamType.Social);
        Register(ManifestOperationType.CompetitionRevealResults, ManifestStreamType.Social);
        Register(ManifestOperationType.CreateChannel, ManifestStreamType.Social);
        Register(ManifestOperationType.PostMessage, ManifestStreamType.Social);
    }

    public static void Register(string operationType, ManifestStreamType streamType)
    {
        _registry[operationType] = streamType;
    }

    public static ManifestStreamType GetStreamType(string operationType)
    {
        if (_registry.TryGetValue(operationType, out var streamType))
        {
            return streamType;
        }

        // Fallback to Content stream type for unknown operations
        return ManifestStreamType.Content;
    }
}

public static class ManifestOperationType
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    /// <summary>Records that the user played a track. Rate-capped during manifest merge.</summary>
    public const string Play = "Play";
    /// <summary>Records that the local user follows a peer (TargetId = peer UserId).</summary>
    public const string Follow = "Follow";
    /// <summary>Records that the local user unfollowed a peer (TargetId = peer UserId).</summary>
    public const string Unfollow = "Unfollow";
    /// <summary>Broadcasts the user's profile fields (IsArtist, Bio, BannerImageHash, Website, DisplayName).</summary>
    public const string Profile = "Profile";
    /// <summary>Signed user-authored comment operation on a track (supports ReplyToId threading).</summary>
    public const string Comment = "Comment";
    /// <summary>Signed soft-delete for a previously authored comment operation.</summary>
    public const string CommentDelete = "CommentDelete";
    /// <summary>Signed social graph operation: add friend relation to another user.</summary>
    public const string FriendAdd = "FriendAdd";
    /// <summary>Signed social graph operation: remove friend relation from another user.</summary>
    public const string FriendRemove = "FriendRemove";
    /// <summary>Signed social graph operation: join a group.</summary>
    public const string GroupJoin = "GroupJoin";
    /// <summary>Signed social graph operation: leave a group.</summary>
    public const string GroupLeave = "GroupLeave";
    /// <summary>Signed user reaction operation: like a track.</summary>
    public const string Like = "Like";
    /// <summary>Signed user reaction operation: remove like from a track.</summary>
    public const string Unlike = "Unlike";
    /// <summary>Signed administrative operation to start a new competition.</summary>
    public const string CreateCompetition = "CreateCompetition";
    /// <summary>Signed member operation to submit a track to a competition.</summary>
    public const string CompetitionSubmit = "CompetitionSubmit";
    /// <summary>Signed member operation to cast a sealed vote in a competition.</summary>
    public const string CompetitionCastVote = "CompetitionCastVote";
    /// <summary>Signed administrative operation to reveal and certify competition results.</summary>
    public const string CompetitionRevealResults = "CompetitionRevealResults";
    /// <summary>Signed administrative operation to create a new group channel.</summary>
    public const string CreateChannel = "CreateChannel";
    /// <summary>Signed user operation to post a message to a group channel.</summary>
    public const string PostMessage = "PostMessage";
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
