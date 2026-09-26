# MeshWave P2P Exchange Protocol

This document describes the protocol used by MeshWave peers to exchange metadata (manifests) and discover each other (PEX).

## Overview

MeshWave uses a decentralized, manifest-based synchronization model. Each user maintains a signed, append-only log of operations (Create, Update, Delete, Follow, Like, Comment, etc.). Synchronization involves exchanging these manifests between peers to reconstruct the global state of the network.

## Communication

- **Protocol**: TCP
- **Default Port**: 39877
- **Serialization**: JSON
- **Message Format**:
  - 4-byte length prefix (Int32, Little Endian)
  - UTF-8 encoded JSON body

## Request Types (`ManifestRequestType`)

1.  **`GetManifest`**: Requests a manifest from a peer.
    - `StreamType`: Identifies whether to fetch the Content, Interaction, or Social stream.
    - `TargetUserId` or `TargetGroupId`: Identifies the user or community group being requested.
    - `StartSequenceNumber`: Used for delta synchronization.
    - `EndSequenceNumber`: Optional upper bound.
2.  **`PushManifest`**: Proactively sends the local manifest (or a specific stream/group manifest) to a peer. Used when the local state changes.
3.  **`RelayManifestPush`**: Sends a manifest to a bootstrap node to be relayed to followers who cannot be reached directly (e.g., behind NAT).
4.  **`GetPeers`**: Peer Exchange (PEX). Requests a list of known peers from a node.
5.  **`RequestRendezvous`**: Requests a coordinated NAT traversal session via a bootstrap node.
6.  **`RequestContent`**: Requests raw content bytes (e.g., audio files) by content hash.
    - `ChunkOffset` / `ChunkLength` (optional): request a byte range. Without `ChunkOffset` the whole content is sent.
    - The response's `ContentLength` is the number of raw bytes that follow the framed response; `TotalContentLength` is the full content size.
    - A zero-length chunk at offset 0 is used to probe `TotalContentLength` before a parallel download.
    - Peers built before chunking ignore the range and send the whole file; clients slice the requested chunk out of it.
    - Whole-content downloads (`SyncOrchestrator.RequestContentAsync`) are verified against the SHA-256 content hash.
7.  **`Announce`**: Registers the sender (`AnnouncingPeer`) with the receiving node, without sending a manifest.
    - The receiver records the **observed** source IP (never the self-reported one) with the announced port.
    - Port `0` means the sender is outbound-only (no listener). Such peers are listed in PEX but never dialed; they pull updates themselves.
    - A regular peer answers with its own `PeerInfo` in `Peers`, so a peer that uses it as bootstrap learns it as a real peer (UserId + key). A standalone bootstrap answers with an empty list.
    - Peers announce to every bootstrap node on startup and on every bootstrap re-contact (`SecurityLimits.BootstrapRetryIntervalMinutes`), which doubles as the registration heartbeat.

## Distribution Strategies

### Push on Update
Whenever a user performs an action (releases a track, likes a post, etc.), the local `SyncOrchestrator` appends a signed operation to its manifest and immediately pushes the updated manifest to all currently connected mesh peers.

### Relay for NATed Peers (deprecated)
Peers that are not reachable as listeners (outbound-only) push their manifest updates to bootstrap nodes. Other peers can then fetch these "relayed" manifests from the bootstrap nodes using the `TargetUserId` field in a `GetManifest` request.

This makes the bootstrap a data relay, which contradicts the design goal that bootstraps only help establish connections. It is scheduled for removal; see [P2P-Protocol-Review.md](P2P-Protocol-Review.md).

### Periodic Poll / Sync
The `SyncOrchestrator` periodically performs maintenance, which includes:
-   Pulling deltas from all known dialable peers every `SecurityLimits.PeriodicSyncIntervalSeconds` (60 s). This is the only way an outbound-only peer receives updates, because pushes cannot reach it.
-   Performing PEX to discover new peers.
-   Re-contacting bootstrap nodes.

## Delta Synchronization and Compaction
To minimize bandwidth, MeshWave supports delta sync across its multiple streams.

When requesting a manifest stream, a peer specifies a `StartSequenceNumber` one past the highest sequence number it has already evaluated for that user/group (`ManifestManager.GetHeadSequenceNumber`). This is not the operation count: operations can be discarded during merge (e.g. the daily play cap), leaving holes. Merges never accept an operation that would leave a gap in the chain. The server then only returns operations with a sequence number greater than or equal to the requested start.

If the requested `StartSequenceNumber` is significantly behind the server's current state, and the server has generated a `ManifestSnapshot` that covers the missing history, the server will return the `ManifestSnapshot` as the baseline. The requesting peer validates the snapshot's signature to securely update its base state (e.g., squashing thousands of historic `Play` operations into the updated totals in the snapshot), and then applies the remaining linear operations on top of it.

## Social Actions and Metadata
Social actions like `Play`, `Like`, `Comment`, and `Follow` are represented as standard `ManifestOperation` entries.
-   **Announcements**: Creating a track or album is a `Create` operation with `TargetType` "Track" or "Album".
-   **Engagement**: `Like`, `Comment`, and `Play` operations reference a `TargetId` (e.g., a track's unique ID).
-   **Identity**: `Profile` operations distribute user metadata (display name, bio, public key).

## Security and Verification
-   A `UserId` is derived from the user's public key. Any key presented for a user (in a push, an announcement or a PEX entry) is only accepted if it hashes to that `UserId` (`CryptoService.IsPublicKeyForUser`).
-   All operations are signed with the user's private key.
-   Peers verify the signature of every operation against the user's public key before merging it into their local store.
-   Protocol limits (message size, operation count) are strictly enforced to prevent DoS attacks.

## P2P Networking Concepts

### Bootstrap
A bootstrap node serves as a lightweight entry point to the MeshWave network. Peers connect to bootstrap nodes upon startup to retrieve an initial list of active peers (PEX). Bootstrap nodes do not relay content or store persistent mesh state; they purely facilitate initial peer discovery, ensuring new nodes can quickly embed themselves into the mesh. A peer can optionally be configured to act as a bootstrap node by running with a fixed, publicly accessible port.

### Peer Connections
MeshWave peers establish connections directly with each other to exchange metadata and manifests. To handle NAT and firewall traversal:
- If at least one peer has a publicly accessible NAT port, a connection is trivially established.
- If both peers are behind restrictive NATs, MeshWave utilizes UDP hole punching mediated by a known peer or bootstrap node to open a direct communication channel.

### Content Downloading
Content distribution in MeshWave is fully decentralized. Content can be requested from any peer hosting the corresponding content hash, not just the original creator. This improves network resilience and availability.

### Lan Discovery
Lan discovery is intended for testing purposes or for local networks. The `PeerDiscovery` class manages LAN peer discovery by broadcasting UDP packets locally and listening for announcements. It allows local peers to connect directly without relying on external bootstrap nodes.

### Responsible Classes
- **Bootstrap:** `BootstrapCoordinator` manages bootstrap nodes, and `PeerRouter` resolves nodes using bootstrap lists.
- **Peer Connections:** `ManifestExchangeClient` and `ManifestExchangeServer` manage TCP exchanges, while `NatTraversalService` handles UDP hole punching.
- **Content Downloading:** `ManifestExchangeClient` performs content requests via `RequestContentAsync`.
- **Lan Discovery:** `PeerDiscovery` broadcasts and listens for UDP peer announcements locally.

#### Load Balancing & Sequential Downloading (Planned)
Future protocol enhancements aim to introduce distributed search across the mesh to locate all peers holding specific content. The network will establish load-balancing protocols to request chunked byte-ranges concurrently across multiple peers. For media playback, chunk requests will be prioritized sequentially to enable instant playback before the full file is downloaded.
