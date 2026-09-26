# MeshWave P2P Exchange Protocol

This document describes the protocol used by MeshWave peers to exchange metadata (manifests) and discover each other (PEX).

## Overview

MeshWave uses a decentralized, manifest-based synchronization model. Each user maintains a signed, append-only log of operations (Create, Update, Delete, Follow, Like, Comment, etc.). Synchronization involves exchanging these manifests between peers to reconstruct the global state of the network.

## Communication

- **Protocol**: TCP, plus UDP (same port number) for NAT introductions and hole-punched sessions
- **Default Port**: 39877
- **Serialization**: Protobuf (`MeshWave.Common.Core/Protos/meshwave.proto`); JSON bodies are still accepted on one-shot TCP
- **One-shot message format** (one request and one response per TCP connection):
  - 4-byte length prefix (Int32, Little Endian)
  - message body
- **Session frame format** (persistent sessions, see below): the same 4-byte length prefix on TCP (LiteNetLib messages on UDP), then
  - 1 byte kind: `1` request, `2` response, `3` keepalive ping, `4` pong
  - 4-byte request ID (Int32, Little Endian); a response carries the ID of its request
  - Protobuf `ManifestRequest` / `ManifestResponse` body

### Persistent sessions

Peers keep one long-lived connection per neighbour (`PeerSession`, managed by `PeerSessionManager`). Either side can send requests over it at any time, several at once, so a peer without an open port can still receive pushes over the session it opened.

- A TCP session starts with a one-shot `OpenSession` request; after the acknowledgement the connection switches to session frames.
- A UDP session is a LiteNetLib reliable-ordered connection created by hole punching (see `RequestIntroduction`).
- Both sides then send a `Hello` request carrying their signed `PeerInfo` and a random nonce. The response is the other side's `Hello`, whose `Proof` signs the requester's nonce with the key that owns its UserId. Only then is the session attributed to that UserId. A standalone bootstrap has no identity and answers anonymously.
- A keepalive ping is sent every 25 s (`SecurityLimits.SessionKeepaliveSeconds`); a session that receives nothing for 75 s is closed.
- If both sides dialled each other, each ranks the duplicates by a hash of the two nonces and keeps the same one.
- Every peer keeps a control session with each bootstrap node and sessions with up to 8 dialable neighbours, and tries to get a hole-punched session to every peer without an open port.

## Request Types (`ManifestRequestType`)

1.  **`GetManifest`**: Requests one page of a stream.
    - `StreamType`: Identifies whether to fetch the Content, Interaction, or Social stream.
    - `TargetUserId`: Whose stream. Empty (or the receiver's own UserId) returns the receiver's own stream; any other author is served from the streams the receiver replicates (store-and-forward).
    - `StartSequenceNumber`: One past the requester's head (delta synchronization).
    - `EndSequenceNumber`: Optional upper bound.
    - The response is filled up to `SecurityLimits.MaxManifestPageBytes` (512 KB); `Manifest.HasMore` says there is more after the last operation. It includes the snapshot when the start is at or before it, and the author's public key (`Manifest.AuthorPublicKey`) when the page starts at sequence 0.
2.  **`PushManifest`**: Sends a delta of a stream (the author's own, or one being forwarded) to a peer. The response's `Heads` holds the receiver's head of that stream after merging (sequence -1 if it holds nothing, e.g. because it has no key for the author yet); the sender uses it to send the missing range when the delta did not connect.
3.  **`GetPeers`**: Peer Exchange (PEX). Requests a list of known peers from a node. Each entry's `LastSeen` is when the node last had first-hand evidence the peer was alive.
4.  **`RequestContent`**: Requests raw content bytes (e.g., audio files) by content hash.
    - `ChunkOffset` / `ChunkLength` (optional): request a byte range. Without `ChunkOffset` the whole content is sent.
    - On one-shot TCP, the response's `ContentLength` is the number of raw bytes that follow the framed response. Over a session the bytes are in `ContentBytes` instead (at most 1 MB per response). `TotalContentLength` is the full content size.
    - A zero-length chunk at offset 0 is used to probe `TotalContentLength` before a parallel download.
    - Peers built before chunking ignore the range and send the whole file; clients slice the requested chunk out of it.
    - Whole-content downloads (`SyncOrchestrator.RequestContentAsync`) are verified against the SHA-256 content hash.
5.  **`Announce`**: Registers the sender (`AnnouncingPeer`) with the receiving node, without sending a manifest.
    - The receiver records the **observed** source IP (never the self-reported one) with the announced port.
    - A non-zero port is verified with a **dial-back**: the receiver connects to the observed IP and the announced port and sends a `Ping`. If that fails, the peer is registered with port `0`.
    - Port `0` means the sender is outbound-only (no reachable listener). Such peers are listed in PEX but never dialled; they are reached over sessions.
    - The response carries `ObservedAddress` (the sender's public IP as seen by the receiver) and `DialBackSucceeded`. A peer whose port fails the dial-back shares port 0 in its own record, but keeps announcing the real port to bootstrap nodes so it recovers when the port opens.
    - A regular peer answers with its own `PeerInfo` in `Peers`, so a peer that uses it as bootstrap learns it as a real peer (UserId + key). A standalone bootstrap answers with an empty list.
    - Peers announce to every bootstrap node on startup and on every bootstrap re-contact (`SecurityLimits.BootstrapRetryIntervalMinutes`), which doubles as the registration heartbeat.
6.  **`OpenSession`**: One-shot only. Upgrades the TCP connection to a persistent session.
7.  **`Ping`**: Liveness probe, used by the dial-back.
8.  **`Hello`**: Session only. Identity handshake (see Persistent sessions).
9.  **`RequestIntroduction`**: Session only. Asks an introducer (any peer with an open port, or the bootstrap) to introduce the sender to `Introduction.TargetUserId`. The introducer needs a session with the target. It returns a single-use `Token` and its UDP port.
10. **`IntroductionOffer`**: Session only. The introducer tells the target who wants to connect, with the same token.
    - Both peers then send LiteNetLib NAT introduce requests with the token to the introducer's UDP port. The introducer observes both public UDP endpoints and sends each peer the other's public and private endpoint; both punch and connect at the same time, and the connection becomes a UDP session.
11. **`GetHeads`**: Returns the head (`StreamHead`: author, stream, head sequence number, head hash) of every stream the receiver holds, at most `SecurityLimits.MaxHeadsPerExchange`. Used for anti-entropy. A standalone bootstrap answers `Acknowledged = false`.

Request type numbers 3 (`RequestRendezvous`) and 5 (`RelayManifestPush`) are retired and answered with `Acknowledged = false`.

### Peer records

`PeerInfo` records are signed by their owner (`PeerRecords`): the signature covers UserId, address, port and `SignedAtUtc`, and is checked against the key that owns the UserId. A node that forwards a record keeps the signature only while it still matches the address and port it forwards. Receivers apply these rules (`PeerRouter`):
- Direct contact (an announcement, a session, a successful request) refreshes a peer's `LastSeen`, and the observed address wins.
- Hearsay (PEX and bootstrap lists) can add an unknown peer, but only a validly signed record that is newer than our own observation can change a known peer's address, or move its `LastSeen` (up to its signing time).
- Peers drop out of the routing table after 12 minutes (`SecurityLimits.PeerLivenessTimeoutMinutes`) without such evidence.

## Distribution Strategies

### Push deltas on update
Whenever a user performs an action (releases a track, likes a post, etc.), the local `SyncOrchestrator` appends a signed operation to its stream, appends it to disk, and schedules a push. Operations recorded within `SecurityLimits.FanoutDebounceMs` (1.5 s) go out together. Each neighbour gets only the operations it lacks (from its last reported head), over a session if there is one (this includes peers without an open port), otherwise by dialling the peer's open port. Our own operations go to every peer with a session plus up to `SecurityLimits.GossipFanout` dialable ones. Over an authenticated session the sender is identified by UserId only; otherwise it sends its signed peer record.

### Store-and-forward gossip
Every peer stores the streams it receives and serves them to others (`GetManifest` with `TargetUserId`). A peer that receives new operations by push forwards them to `SecurityLimits.GossipFanout` (6) neighbours, preferring session peers and skipping the sender and the author. A peer that already has them merges nothing and does not forward them further. Only fresh deltas (at most 64 operations) are gossiped; catch-up goes through anti-entropy. Because operations are signed individually, a forwarding peer cannot alter or forge them. A peer stores the streams of at most `SecurityLimits.MaxReplicatedAuthors` authors.

### No relay by the bootstrap
Standalone bootstrap nodes never store, relay or serve manifests or content. Peers that cannot reach each other directly, even with hole punching (symmetric NAT on both sides), still get each other's manifests from any common neighbour (store-and-forward of signed data, above). Content comes from any other peer that holds the hash.

### Anti-entropy
The `SyncOrchestrator` periodically performs maintenance, which includes:
-   Every `SecurityLimits.PeriodicSyncIntervalSeconds` (5 minutes), a heads exchange (`GetHeads`) with every session peer and a few dialable ones. It pulls, page by page, the streams where the neighbour is ahead (at most `SecurityLimits.MaxStreamsPulledPerRound` per round) and pushes our own streams the neighbour lacks. The same exchange runs when a peer is discovered and when a session is established.
-   Opening missing sessions (every 20 s): bootstrap control sessions, dialable neighbours, and introductions to peers without an open port.
-   Performing PEX to discover new peers.
-   Re-contacting bootstrap nodes.

## Streams, Delta Synchronization and Compaction
Each author has three streams (Content, Interaction, Social). A stream is an append-only, **hash-linked log**. Every operation signs its sequence number, all of its fields including all metadata, and `PrevHash`, the hash of the author's previous operation in the stream (`ManifestManager.ComputeOperationHash`). The first operation's `PrevHash` is a genesis hash of (author, stream), so an operation cannot be replayed into another stream.

When requesting a manifest stream, a peer specifies a `StartSequenceNumber` one past its head for that author and stream (`ManifestManager.GetHeadSequenceNumber`). Merges only append an operation that is the next sequence number **and** chains to the local head, so a stream never has gaps. Peers keep every operation that verifies, so a replicated stream is identical to the author's log. Rules that depend on the reader are applied when reading (`ManifestState`): the daily play cap when counting plays, competition deadlines when tallying votes. If a peer offers a different operation for a sequence number already held (the author signed two versions: a **fork**), the first version is kept and the conflict is reported (`ManifestManager.ForkDetected`, peer diagnostics).

If the requested `StartSequenceNumber` is at or before the author's `ManifestSnapshot`, the snapshot is returned as the baseline. The snapshot is signed, covers everything up to `LastSequenceNumber`, and signs `HeadHash` (the hash of its last operation), so the operations after it chain from it. The requesting peer validates the snapshot's signature to update its base state (e.g., squashing thousands of historic `Play` operations into the totals in the snapshot) and then applies the remaining operations on top of it. Local operations that continue the adopted snapshot are kept.

Compaction (at 500 operations, keeping the latest 100) preserves comments, group and competition operations in the snapshot, but at most `SecurityLimits.MaxSnapshotRetainedOperations` (1,000) comments and group posts; older ones are dropped. Consumers read preserved and live operations together through `Manifest.AllOperations()`.

Streams are stored as append-only JSON-lines logs (`ManifestLog`, `*.mwlog`): new operations are appended, and the file is only rewritten when the snapshot changes.

## Social Actions and Metadata
Social actions like `Play`, `Like`, `Comment`, and `Follow` are represented as standard `ManifestOperation` entries.
-   **Announcements**: Creating a track or album is a `Create` operation with `TargetType` "Track" or "Album".
-   **Engagement**: `Like`, `Comment`, and `Play` operations reference a `TargetId` (e.g., a track's unique ID).
-   **Identity**: `Profile` operations distribute user metadata (display name, bio, public key).

## Security and Verification
-   A `UserId` is derived from the user's public key. Any key presented for a user (in a push, an announcement or a PEX entry) is only accepted if it hashes to that `UserId` (`CryptoService.IsPublicKeyForUser`).
-   All operations are signed with the user's private key, including all of their metadata (titles, artists, cover and icon hashes, shader scripts), so a peer that forwards them cannot alter them.
-   Peers verify the signature of every operation, and that it chains to the previous one, against the author's public key before merging it into their local store. The author's key only comes from a source whose key hashes to the author's UserId: a stored stream, the routing table, the sender if it is the author, or `Manifest.AuthorPublicKey`.
-   Signatures are base64 in the model and in storage, and raw bytes on the wire.
-   Protocol limits (message size, operation count) are strictly enforced to prevent DoS attacks.

## P2P Networking Concepts

### Bootstrap
A bootstrap node serves as a lightweight entry point to the MeshWave network. Peers connect to bootstrap nodes upon startup to retrieve an initial list of active peers (PEX), keep a control session open with them, and use them as introducers for hole punching. Bootstrap nodes do not relay content or manifests or store persistent mesh state; they only help peers find and reach each other. A peer can act as a bootstrap node by running with a fixed, publicly accessible port; every peer with an open port is also an introducer.

### Peer Connections
MeshWave peers establish connections directly with each other to exchange metadata and manifests. To handle NAT and firewall traversal:
- If at least one peer has a publicly accessible port (possibly mapped with UPnP/NAT-PMP and verified by the bootstrap's dial-back), the other peer dials it and keeps a persistent TCP session, which both sides use.
- If neither has one, an introducer (a peer with an open port, or the bootstrap) introduces them over UDP and they punch through to each other, then keep a persistent UDP session.
- If both are behind symmetric NATs, punching usually fails; they do not connect directly.

### Content Downloading
Content distribution in MeshWave is fully decentralized. Content can be requested from any peer hosting the corresponding content hash, not just the original creator. This improves network resilience and availability.

### Lan Discovery
Lan discovery is intended for testing purposes or for local networks. The `PeerDiscovery` class manages LAN peer discovery by broadcasting UDP packets locally and listening for announcements. It allows local peers to connect directly without relying on external bootstrap nodes.

### Responsible Classes
- **Bootstrap:** `BootstrapCoordinator` manages bootstrap nodes, and `PeerRouter` resolves nodes using bootstrap lists.
- **Peer Connections:** `PeerSessionManager` owns persistent sessions (`PeerSession` over `TcpFrameTransport` or `UdpFrameTransport`) and introductions; `UdpSessionHost` does the UDP introducing and punching (LiteNetLib). `ManifestExchangeClient` sends requests (over a session when one exists), `ManifestExchangeServer` answers them. `NatTraversalService` sets up UPnP/NAT-PMP port mappings.
- **Manifest Replication:** `ManifestManager` signs, verifies, merges and pages hash-linked streams; `SyncOrchestrator` (`SyncOrchestrator.Replication.cs`) pushes deltas, forwards gossip and runs the heads exchange; `PeerManifestStore` stores replicated streams as `ManifestLog` files; `ManifestState` interprets streams for readers.
- **Content Downloading:** `ManifestExchangeClient` performs content requests via `RequestContentAsync`.
- **Lan Discovery:** `PeerDiscovery` broadcasts and listens for UDP peer announcements locally.

#### Load Balancing & Sequential Downloading (Planned)
Future protocol enhancements aim to introduce distributed search across the mesh to locate all peers holding specific content. The network will establish load-balancing protocols to request chunked byte-ranges concurrently across multiple peers. For media playback, chunk requests will be prioritized sequentially to enable instant playback before the full file is downloaded.
