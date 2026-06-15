using System.Collections.Generic;

namespace MeshWave.Synchronizer;

public sealed class PeerDiagnosticsSnapshot
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool IsOnline { get; init; }
    public bool IsBootstrap { get; init; }
    public bool HasManifest { get; init; }
    public int PublishedTrackCount { get; init; }
    public int PublishedAlbumCount { get; init; }
    public int OperationCount { get; init; }
    public IReadOnlyList<PeerMessageLogEntry> RecentMessages { get; init; } = [];
}
