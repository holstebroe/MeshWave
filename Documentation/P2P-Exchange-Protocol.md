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

## Distribution Strategies

### Push on Update
Whenever a user performs an action (releases a track, likes a post, etc.), the local `SyncOrchestrator` appends a signed operation to its manifest and immediately pushes the updated manifest to all currently connected mesh peers.

### Relay for NATed Peers
Peers that are not reachable as listeners (outbound-only) push their manifest updates to bootstrap nodes. Other peers can then fetch these "relayed" manifests from the bootstrap nodes using the `TargetUserId` field in a `GetManifest` request.

### Periodic Poll / Sync
The `SyncOrchestrator` periodically performs maintenance, which includes:
-   Syncing with all known peers to ensure no updates were missed.
-   Performing PEX to discover new peers.
-   Re-contacting bootstrap nodes.

## Delta Synchronization and Compaction
To minimize bandwidth, MeshWave supports delta sync across its multiple streams.

When requesting a manifest stream, a peer specifies a `StartSequenceNumber` based on the last operation it has already received and verified for that user/group. The server then only returns operations with a sequence number greater than or equal to the requested start.

If the requested `StartSequenceNumber` is significantly behind the server's current state, and the server has generated a `ManifestSnapshot` that covers the missing history, the server will return the `ManifestSnapshot` as the baseline. The requesting peer validates the snapshot's signature to securely update its base state (e.g., squashing thousands of historic `Play` operations into the updated totals in the snapshot), and then applies the remaining linear operations on top of it.

## Social Actions and Metadata
Social actions like `Play`, `Like`, `Comment`, and `Follow` are represented as standard `ManifestOperation` entries.
-   **Announcements**: Creating a track or album is a `Create` operation with `TargetType` "Track" or "Album".
-   **Engagement**: `Like`, `Comment`, and `Play` operations reference a `TargetId` (e.g., a track's unique ID).
-   **Identity**: `Profile` operations distribute user metadata (display name, bio, public key).

## Security and Verification
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
