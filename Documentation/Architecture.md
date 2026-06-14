# MeshWave Architecture

## Solution Structure

MeshWave is organized into these projects:

- `MeshWave` (WPF app): UI, navigation, playback experience
- `MeshWave.LibraryManager`: import, indexing, metadata/cache management
- `MeshWave.Synchronizer`: P2P sync layer (in progress)
- `MeshWave.Common.Core`: shared domain models, crypto, storage abstractions

## Storage Model

### AppData (application state)

`the base data folder\`

- `settings.json` (persisted app settings)
- profile/settings-related files (planned expansion)

### Base folder (user-configurable)

`{BaseFolder}` (configured in Settings)

- `Local Music/` (user-managed music)
- `Peer Music/` (community-managed music)

Music is organized as:

`{Artist}/{Album or _singles_}/`

Inside each album folder:

- audio files
- `.cache/` (metadata cache, cover images, waveform cache)
- `.comments/` (track/album comment data)

## P2P Manifest Architecture

MeshWave uses an **append-only signed log** model for all user-authored metadata and social interactions. Every peer maintains local manifests that serve as their public record of state changes.

### Manifest Structure

A `Manifest` is a collection of `ManifestOperation` entries associated with a `UserId`. To improve scalability and synchronization efficiency, user manifests are segmented into three distinct streams (`ManifestStreamType`):
- **Content**: Tracks, Albums, Playlists (`Create`, `Update`, `Delete`).
- **Interaction**: Engagement metrics like `Play`, `Like`, `Comment`.
- **Social**: User profile updates and social graph connections (`Follow`, `FriendAdd`, `GroupJoin`).

Additionally, MeshWave supports **GroupManifests** for community-owned groups (e.g., chat channels, forums). These function similarly to user manifests but are associated with a `GroupId` and use `GroupOperationType` (e.g., `Found`, `Join`, `Post`, `Moderate`).

- **Manifest**: Contains `UserId` (or `GroupId`), a `StreamType`, a `List<ManifestOperation>`, a `Version` counter, a `LastUpdated` timestamp, and optionally a `ManifestSnapshot`.
- **ManifestOperation**: A single atomic change.
    - `OperationId`: Unique GUID.
    - `OperationType`: Enum (Create, Update, Delete, Play, Follow, Profile, Comment, Like, etc.).
    - `TargetId` / `TargetType`: The entity being acted upon (e.g., Track ID, User ID).
    - `SequenceNumber`: Monotonically increasing index (0, 1, 2...).
    - `Timestamp`: UTC time of the operation.
    - `Signature`: RSA signature of the operation fields, signed by the user's private key.
    - `Metadata`: Key-value pairs for operation-specific data (e.g., track title, comment text).
- **ManifestSnapshot**: A compressed, verified state of a manifest up to a specific `LastSequenceNumber`, enabling delta synchronization.

### Lifecycle and Management

1.  **Creation and Signing**: When a user performs an action (e.g., plays a track or follows a peer), the `ManifestManager` creates a new operation, assigns the next `SequenceNumber`, and signs it using the user's RSA private key.
2.  **Persistence**: The local manifest is persisted to `the base data folder\LocalManifests\{UserId}.json`. Remote peer manifests are stored in `the base data folder\PeerManifests\{UserId}.json`.
3.  **Discovery and Exchange**: Peers discover each other via `PeerRouter` (LAN, Bootstrap, PEX). Once connected, they exchange manifests over TCP using `ManifestExchangeClient` and `ManifestExchangeServer`.
    - **Push**: A peer can proactively push its manifest to known peers.
    - **Fetch**: A peer can request the full manifest from another peer.
4.  **Verification and Merging**: When a manifest is received, `ManifestManager` verifies the RSA signature of every operation and ensures `SequenceNumber` continuity. Verified operations are merged into the local cache for that peer.

### Performance and Scalability Analysis

The architecture addresses long-term performance risks (bandwidth exhaustion, memory pressure, operation accumulation) through several key mechanisms:

- **Stream Segmentation**: By separating Content, Interaction, and Social streams, peers can prioritize syncing only what they care about (e.g., only fetching the Content stream for search results).
- **Delta Synchronization**: Peers can request only operations after a specific sequence number, minimizing payload sizes.
- **ManifestSnapshots (Compaction)**: Older operations (e.g., thousands of `Play` events) are periodically squashed into a single, consensus-authenticated `ManifestSnapshot`. This "checkpoint" includes aggregated states like total play counts, preserving the current state without needing the full linear history.

#### Safeguards
-   `SecurityLimits.MaxManifestOperations` (currently 10,000): Rejects manifests exceeding this limit to prevent runaway growth or DoS attacks.
-   `SecurityLimits.MaxMessageBytes` (512 KB): Limits the raw TCP payload size.
-   `ManifestPushCooldownMs` (30s): Prevents rapid-fire updates to the same peer.

#### Future Evolutions
1.  **Binary Wire Formats**: Moving away from JSON to a more compact format (e.g., Protobuf) to reduce transmission size.

## Current UI Architecture

- Shared playback session is managed centrally so playback continues while navigating tabs.
- Library UI is split into:
  - Community Library
  - Local Music (with import workflow)
- Hierarchical browsing flow:
  - Artist -> Album -> Track
- Double-click track starts playback from both Library and Local Music.

## Caching Strategy

- Metadata cache is read first.
- Re-scan is performed only when cache is missing or stale.
- Cover images are extracted and cached.
- Waveform data is loaded from cache when available.
- If waveform cache is missing, waveform is generated in the background during playback and then cached.

## P2P Handshake and Connection Establishment

MeshWave uses an ordered, bandwidth-light connection strategy for peer-to-peer exchanges:

1. **Routing table lookup**
   - Resolve target peer from `PeerRouter` (LAN discovery + bootstrap + PEX cache).
2. **Bootstrap-assisted refresh (low bandwidth)**
   - If peer is missing, query configured bootstrap nodes for updated PEX entries.
   - Bootstrap remains metadata-only: no content relay, only peer endpoint exchange.
3. **Direct TCP reachability probe**
   - Fast probe to peer manifest endpoint to detect immediate reachability.
4. **UDP hole-punch attempt**
   - Both peers send/ack short UDP punch packets (`meshwave:punch`, `meshwave:ack`) to open NAT mappings.
5. **Direct content request**
   - Attempt content retrieval over peer endpoint after probe/punch.
6. **NAT fallback guidance**
   - On failure, emit concrete user instructions with detected local IP and manifest port,
     plus the remote endpoint details used during attempts.

### "Crossing hands" via bootstrap (rendezvous-style)

A known NAT traversal trick is a rendezvous handshake where a public coordinator helps two peers
coordinate simultaneous outbound attempts (sometimes called "crossing hands").

Current MeshWave behavior already includes bootstrap-assisted endpoint refresh and UDP punch attempts.
A future extension can add explicit rendezvous session messages via bootstrap while still keeping
bootstrap bandwidth minimal and content transfer strictly peer-to-peer.

## In Progress

- Rounded user icons for timeline markers/comments
- User profile persistence and icon generation
- Community synchronization and play count sync
- Richer comment interaction (jump-to-marker, editing)
