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
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>The owner's X25519 encryption public key, so others can seal data to them (e.g. a sealed competition vote).</summary>
    public string EncryptionPublicKey { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public List<string> Capabilities { get; set; } = [];

    /// <summary>
    /// When the owner signed <see cref="Address"/> and <see cref="Port"/> (see <see cref="Signature"/>). Null for unsigned records.
    /// </summary>
    public DateTime? SignedAtUtc { get; set; }

    /// <summary>
    /// The owner's signature over UserId, Address, Port, <see cref="EncryptionPublicKey"/> and <see cref="SignedAtUtc"/>.
    /// Only a signed record may change the address, port or encryption key of a peer that is already known, so
    /// relayed (PEX) entries cannot redirect traffic or swap in a different encryption key.
    /// </summary>
    public string Signature { get; set; } = string.Empty;
}

public enum ManifestRequestType
{
    GetManifest = 0,
    PushManifest = 1,
    GetPeers = 2,
    // 3 was RequestRendezvous (removed; replaced by RequestIntroduction).
    RequestContent = 4,
    // 5 was RelayManifestPush (removed; the bootstrap never relays data).

    /// <summary>
    /// Registers the sender (<see cref="ManifestRequest.AnnouncingPeer"/>) with the receiving node without sending a manifest.
    /// The receiver records the observed source IP. A non-zero announced port is verified with a dial-back
    /// (<see cref="Ping"/>); if that fails the peer is registered as outbound-only (port 0).
    /// The response carries the observed address, the dial-back result and, for a regular peer, its own peer info.
    /// </summary>
    Announce = 6,

    /// <summary>
    /// Sent as the first (and only) one-shot message on a TCP connection to turn it into a persistent, multiplexed
    /// session. After the acknowledgement both sides exchange
    /// <see cref="Hello"/> requests over the session.
    /// </summary>
    OpenSession = 7,

    /// <summary>Liveness probe. Used for the dial-back reachability check after <see cref="Announce"/>.</summary>
    Ping = 8,

    /// <summary>Session only: asks the receiver (an introducer) to introduce the sender to <see cref="Introduction.TargetUserId"/> for UDP hole punching.</summary>
    RequestIntroduction = 9,

    /// <summary>Session only: an introducer tells the receiver that <see cref="Introduction.RequesterUserId"/> wants to punch through to it.</summary>
    IntroductionOffer = 10,

    /// <summary>Session only: identity handshake. The response proves key ownership by signing the request's nonce.</summary>
    Hello = 11,

    /// <summary>
    /// Anti-entropy: asks for the heads (<see cref="StreamHead"/>) of every stream the receiver holds, its own and the ones
    /// it replicates for other authors. The requester then pulls only the streams where it is behind.
    /// </summary>
    GetHeads = 12
}

public class ManifestRequest
{
    public ManifestRequestType Type { get; set; }
    public ManifestStreamType StreamType { get; set; } = ManifestStreamType.Content;
    public Manifest? Manifest { get; set; }
    public string? ContentHash { get; set; }
    public PeerInfo? AnnouncingPeer { get; set; }
    public int StartSequenceNumber { get; set; }
    public int? EndSequenceNumber { get; set; }
    public long? ChunkOffset { get; set; }
    public long? ChunkLength { get; set; }
    public SessionHello? Hello { get; set; }
    public Introduction? Introduction { get; set; }

    /// <summary>
    /// <see cref="ManifestRequestType.GetManifest"/>: whose stream to return. Null or the receiver's own UserId returns the
    /// receiver's own stream; any other author is served from the streams the receiver replicates (store-and-forward).
    /// </summary>
    public string? TargetUserId { get; set; }
}

public class ManifestResponse
{
    public Manifest? Manifest { get; set; }
    public bool Acknowledged { get; set; }
    public List<PeerInfo> Peers { get; set; } = [];
    public byte[]? ContentBytes { get; set; }
    public long ContentLength { get; set; }
    public long? TotalContentLength { get; set; }
    public SessionHello? Hello { get; set; }
    public Introduction? Introduction { get; set; }

    /// <summary>The requester's source address as the responder observed it (lets peers learn their public IP).</summary>
    public string? ObservedAddress { get; set; }

    /// <summary>Announce only: whether the responder could connect back to the announced port. Null when not checked.</summary>
    public bool? DialBackSucceeded { get; set; }

    /// <summary>
    /// <see cref="ManifestRequestType.GetHeads"/>: every stream the responder holds.
    /// <see cref="ManifestRequestType.PushManifest"/>: the responder's head of the pushed stream after merging, so the sender
    /// knows which operations to send next time (or has to send now, when the push did not connect to what the responder had).
    /// </summary>
    public List<StreamHead> Heads { get; set; } = [];

    /// <summary>
    /// <see cref="ManifestRequestType.RequestContent"/>: the Merkle sibling-hash proof for this chunk (see
    /// <see cref="MeshWave.Common.Core.Crypto.ContentMerkleTree"/>), present only when the request's chunk offset and
    /// length exactly align to one chunk boundary. Null when the responder does not (or cannot) compute a proof;
    /// the requester then falls back to verifying the whole download's hash once it is complete (F2).
    /// </summary>
    public List<byte[]>? ChunkMerkleProof { get; set; }
}

/// <summary>
/// One byte-range slice of local content served to a peer (<see cref="ManifestRequestType.RequestContent"/>),
/// together with the content's total length and, when the slice aligns to a chunk boundary, a Merkle proof for it.
/// </summary>
public sealed class ContentSlice
{
    public required long TotalLength { get; init; }
    public required byte[] Bytes { get; init; }

    /// <summary>Sibling-hash proof for this chunk (see <see cref="MeshWave.Common.Core.Crypto.ContentMerkleTree"/>), or null when this slice does not align to one chunk.</summary>
    public IReadOnlyList<byte[]>? MerkleProof { get; init; }
}

/// <summary>
/// Session identity handshake. Each side sends one in a <see cref="ManifestRequestType.Hello"/> request with a fresh
/// <see cref="Nonce"/>; the other side answers with its own hello whose <see cref="Proof"/> signs that nonce.
/// A node without an identity (a standalone bootstrap) answers with an empty <see cref="Peer"/> and no proof.
/// </summary>
public class SessionHello
{
    public PeerInfo? Peer { get; set; }
    public long Nonce { get; set; }
    public string Proof { get; set; } = string.Empty;

    /// <summary>The UDP port on which the sender accepts NAT introduction requests; 0 if it cannot introduce.</summary>
    public int UdpPort { get; set; }

    /// <summary>Whether the sender introduces peers to each other (<see cref="ManifestRequestType.RequestIntroduction"/>).</summary>
    public bool IsIntroducer { get; set; }
}

/// <summary>
/// Introduction of two peers for UDP hole punching. Both peers send a NAT introduce request carrying <see cref="Token"/>
/// to the introducer's UDP port; the introducer then tells each one the other's public and private UDP endpoint.
/// </summary>
public class Introduction
{
    public string RequesterUserId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int IntroducerUdpPort { get; set; }
}
