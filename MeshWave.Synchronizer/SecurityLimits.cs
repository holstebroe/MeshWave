using MeshWave.Common.Core.P2P;
namespace MeshWave.Synchronizer;

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
    public const int MaxMetadataValueLength = 512;
    public const int MaxMetadataEntries = 20;

    // --- Rate limiting ---

    /// <summary>Minimum milliseconds between manifest pushes to the same peer.</summary>
    public const int ManifestPushCooldownMs = 30_000;

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

    // --- Connection timeouts ---

    public const int ConnectTimeoutMs = 8_000;
    public const int ReadTimeoutMs = 15_000;

    // --- Validation helpers ---

    public static bool IsValidDisplayName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxDisplayNameLength;

    public static bool IsValidUserId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxTargetIdLength;

    public static bool IsValidContentHash(string? value) =>
        value == null || value.Length <= MaxContentHashLength;

    public static string Truncate(string? value, int maxLength) =>
        value == null ? string.Empty :
        value.Length <= maxLength ? value : value[..maxLength];
}
