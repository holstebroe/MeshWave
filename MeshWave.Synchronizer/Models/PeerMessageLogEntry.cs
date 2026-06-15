using System;

namespace MeshWave.Synchronizer;

public sealed class PeerMessageLogEntry
{
    public DateTime TimestampUtc { get; init; }
    public string MessageType { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Details { get; init; } = string.Empty;
}
