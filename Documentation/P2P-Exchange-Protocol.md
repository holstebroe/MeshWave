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
    - `StartSequenceNumber`: Used for delta synchronization.
    - `EndSequenceNumber`: Optional upper bound.
    - `TargetUserId`: Optional. Used when requesting a manifest from a relay/bootstrap node.
2.  **`PushManifest`**: Proactively sends the local manifest to a peer. Used when the local state changes.
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

## Delta Synchronization
To minimize bandwidth, MeshWave supports delta sync. When requesting a manifest, a peer specifies a `StartSequenceNumber` based on the last operation it has already received and verified for that user. The server then only returns operations with a sequence number greater than or equal to the requested start.

## Social Actions and Metadata
Social actions like `Play`, `Like`, `Comment`, and `Follow` are represented as standard `ManifestOperation` entries.
-   **Announcements**: Creating a track or album is a `Create` operation with `TargetType` "Track" or "Album".
-   **Engagement**: `Like`, `Comment`, and `Play` operations reference a `TargetId` (e.g., a track's unique ID).
-   **Identity**: `Profile` operations distribute user metadata (display name, bio, public key).

## Security and Verification
-   All operations are signed with the user's private key.
-   Peers verify the signature of every operation against the user's public key before merging it into their local store.
-   Protocol limits (message size, operation count) are strictly enforced to prevent DoS attacks.
