using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.P2P;

/// <summary>
/// Represents information about a discovered peer.
/// </summary>
public class PeerInfo
{
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Address { get; set; }
    public int Port { get; set; }
    public string PublicKeyPem { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public List<string> Capabilities { get; set; } = [];
}

public enum ManifestRequestType
{
    GetManifest,
    PushManifest,
    GetPeers,
    RequestRendezvous,
    RequestContent,
    RelayManifestPush,
    NotifyNewOperation
}

public class ManifestRequest
{
    public ManifestRequestType Type { get; set; }
    public ManifestStreamType StreamType { get; set; } = ManifestStreamType.Content;
    public Manifest? Manifest { get; set; }
    public RendezvousRequest? Rendezvous { get; set; }
    public string? ContentHash { get; set; }
    public PeerInfo? AnnouncingPeer { get; set; }
    public int StartSequenceNumber { get; set; }
    public int? EndSequenceNumber { get; set; }
    public string? TargetUserId { get; set; }
}

public class ManifestResponse
{
    public Manifest? Manifest { get; set; }
    public bool Acknowledged { get; set; }
    public List<PeerInfo> Peers { get; set; } = [];
    public RendezvousResponse? Rendezvous { get; set; }
    public byte[]? ContentBytes { get; set; }
    public long ContentLength { get; set; }
}

public class RendezvousRequest
{
    public string InitiatorUserId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public int InitiatorPort { get; set; }
    public int RequestedProbeWindowMs { get; set; } = 4_000;
}

public class RendezvousResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime ProbeStartUtc { get; set; }
    public int ProbeWindowMs { get; set; } = 4_000;
    public string Message { get; set; } = string.Empty;
}
