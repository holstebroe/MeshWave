namespace MeshWave.Common.Core;

/// <summary>
/// Central security constants for all P2P protocol limits.
/// Any incoming data exceeding these values must be rejected immediately.
/// </summary>
public static class SecurityLimits
{
    // --- Network message limits ---

    /// <summary>Maximum raw TCP message body size in bytes (Protobuf optimized to 512 KB).</summary>
    public const int MaxMessageBytes = 2 * 1024 * 1024;

    /// <summary>Maximum number of operations allowed in a manifest received from a peer.</summary>
    public const int MaxManifestOperations = 10_000;

    /// <summary>Maximum number of peers returned in a single PEX response.</summary>
    public const int MaxPeersPerExchange = 50;

    /// <summary>Maximum number of peers the router will maintain in its active table.</summary>
    public const int MaxRoutingTableSize = 500;

    /// <summary>Maximum number of bootstrap nodes configurable by the user.</summary>
    public const int MaxBootstrapNodes = 20;

    /// <summary>Maximum inbound connections accepted per minute from a single IP.</summary>
    public const int MaxConnectionsPerMinutePerIp = 10;

    /// <summary>
    /// Maximum PushManifest requests accepted per minute from a single sender (IP for one-shot TCP, or the
    /// authenticated peer's address for a session). Bounds the cost a single misbehaving or compromised peer can
    /// impose through the store-and-forward gossip of S2. Generous enough for legitimate bursts (many local
    /// operations are already batched into one delta by <see cref="FanoutDebounceMs"/>).
    /// </summary>
    public const int MaxPushesPerMinutePerSender = 600;

    /// <summary>
    /// Maximum GetManifest/GetHeads (pull) requests accepted per minute from a single sender. Sized to allow a
    /// peer that just joined to catch up on many streams in one anti-entropy round (<see cref="MaxStreamsPulledPerRound"/>)
    /// while still bounding a flood of pull requests from a misbehaving peer.
    /// </summary>
    public const int MaxPullsPerMinutePerSender = 600;

    // --- String field limits (characters) ---

    public const int MaxDisplayNameLength = 64;
    public const int MaxUserDescriptionLength = 512;
    public const int MaxTrackTitleLength = 256;
    public const int MaxAlbumNameLength = 256;
    public const int MaxArtistNameLength = 256;
    public const int MaxCommentTextLength = 2_000;
    public const int MaxVersionStringLength = 32;
    public const int MaxContentHashLength = 128;
    public const int MaxOperationIdLength = 64;
    public const int MaxTargetTypeLength = 32;
    public const int MaxTargetIdLength = 64;
    public const int MaxMetadataKeyLength = 64;
    public const int MaxMetadataValueLength = 2048;
    public const int MaxMetadataEntries = 20;

    // --- Manifest replication ---

    /// <summary>
    /// Operations recorded within this window are pushed together as one delta. Keeps bursts (several likes, a comment thread)
    /// to one message per neighbour while still delivering a single comment within a few seconds.
    /// </summary>
    public const int FanoutDebounceMs = 1_500;

    /// <summary>
    /// Number of peers a newly received operation is forwarded to (store-and-forward gossip). Peers not reached this way
    /// get the operation through anti-entropy (<see cref="PeriodicSyncIntervalSeconds"/>).
    /// </summary>
    public const int GossipFanout = 6;

    /// <summary>Operations per manifest page are added until the page reaches this size; the rest is fetched with further requests.</summary>
    public const int MaxManifestPageBytes = 512 * 1024;

    /// <summary>Maximum number of stream heads in one heads exchange.</summary>
    public const int MaxHeadsPerExchange = 4_000;

    /// <summary>Maximum number of streams pulled from one peer in one anti-entropy round.</summary>
    public const int MaxStreamsPulledPerRound = 200;

    /// <summary>
    /// Maximum number of authors whose streams a peer replicates. Authors beyond this are not stored, so that cheaply
    /// generated identities cannot fill a peer's disk.
    /// </summary>
    public const int MaxReplicatedAuthors = 5_000;

    /// <summary>
    /// Maximum number of comments and group posts a snapshot preserves. Compaction drops the oldest beyond this, which bounds
    /// the snapshot (and the first sync of a new peer) no matter how long a user has been active.
    /// </summary>
    public const int MaxSnapshotRetainedOperations = 1_000;

    // --- Rate limiting ---

    /// <summary>
    /// Maximum play-count operations a single user may contribute per track per UTC day.
    /// Operations beyond this cap are dropped during MergeManifest to prevent inflation.
    /// </summary>
    public const int MaxPlaysPerUserPerTrackPerDay = 3;

    /// <summary>
    /// How often (in minutes) the router re-contacts bootstrap nodes during the maintenance loop.
    /// Ensures peers can rejoin after a bootstrap node restart without restarting the app.
    /// </summary>
    public const int BootstrapRetryIntervalMinutes = 5;

    /// <summary>
    /// How often (in seconds) the orchestrator pulls manifest deltas from all known peers (anti-entropy).
    /// Updates normally arrive as pushes over persistent sessions, which also reach peers behind NAT;
    /// this pull only repairs anything a push missed.
    /// </summary>
    public const int PeriodicSyncIntervalSeconds = 300;

    /// <summary>
    /// A peer drops out of the routing table when it has not been in direct contact (or re-signed its record) for this long.
    /// Must be longer than <see cref="BootstrapRetryIntervalMinutes"/> so that entries refreshed by the bootstrap heartbeat never flicker.
    /// </summary>
    public const int PeerLivenessTimeoutMinutes = 12;

    /// <summary>How often a peer re-signs its own <c>PeerInfo</c> record, so relayed copies stay fresh.</summary>
    public const int PeerRecordResignMinutes = 4;

    // --- Persistent sessions ---

    /// <summary>Keepalive interval on persistent sessions. Short enough to hold typical NAT mappings (30 s or more) open.</summary>
    public const int SessionKeepaliveSeconds = 25;

    /// <summary>A session that receives nothing (not even a keepalive) for this long is closed.</summary>
    public const int SessionIdleTimeoutSeconds = 75;

    /// <summary>A session must complete its identity handshake within this time.</summary>
    public const int SessionHandshakeTimeoutSeconds = 15;

    /// <summary>Maximum number of persistent sessions a node keeps (inbound and outbound).</summary>
    public const int MaxSessions = 128;

    /// <summary>Maximum number of outbound sessions a peer opens to dialable neighbours (bootstrap control sessions not counted).</summary>
    public const int MaxOutboundNeighbourSessions = 8;

    /// <summary>Timeout for the dial-back reachability check after an Announce.</summary>
    public const int DialBackTimeoutMs = 3_000;

    /// <summary>How long an introducer keeps an introduction token, and how long peers keep punching for it.</summary>
    public const int IntroductionTimeoutSeconds = 12;

    /// <summary>Minimum seconds between two introduction attempts for the same target peer.</summary>
    public const int IntroductionRetryCooldownSeconds = 60;

    // --- Connection timeouts ---

    public const int ConnectTimeoutMs = 8_000;
    public const int ReadTimeoutMs = 15_000;

    // --- Validation helpers ---

    public static bool IsValidDisplayName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= MaxDisplayNameLength;
    }

    public static bool IsValidUserId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= MaxTargetIdLength;
    }

    public static bool IsValidContentHash(string? value)
    {
        return value == null || value.Length <= MaxContentHashLength;
    }

    public static string Truncate(string? value, int maxLength)
    {
        return value == null ? string.Empty :
            value.Length <= maxLength ? value : value[..maxLength];
    }
}
