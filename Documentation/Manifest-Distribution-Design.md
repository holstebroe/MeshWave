# Design Document: Split Manifest and Scalable Distribution

## Overview
As MeshWave grows, the single-manifest strategy faces scalability challenges. This document proposes a multi-stream manifest architecture that segregates data by its nature (static vs. dynamic), size, and interaction frequency.

## 1. Manifest Segmentation
We divide user data into three distinct streams, allowing peers to prioritize synchronization based on their interests and resources.

### A. Content Manifest (Authority: Owner)
Contains the "Canonical" record of the user's creative output.
- **Entities**: Tracks (files and hashes), Track Metadata, Album Metadata, Cover Art (hashes), Playlists.
- **Operations**: `Create`, `Update`, `Delete` (Tombstone), `AnnounceVersion`.
- **Characteristics**:
    - **Low Frequency**: Updated only when the artist releases or edits content.
    - **High Stability**: Content hashes are immutable for a given version.
    - **High Importance**: Required for peers to discover and download music.
- **Authority**: The user's private key is the sole authority. Other peers only replicate and verify.

### B. Content Interaction Manifest (Mixed Authority)
Contains engagement data related to content.
- **Operations**: `Play`, `Comment`, `CommentEdit`, `CommentDelete`, `Like`, `Unlike`.
- **Characteristics**:
    - **High Frequency**: Play counts and comments grow rapidly.
    - **High Volume**: Can reach thousands of entries per user.
    - **Latency Requirement**: Comments should be distributed to peers within 1 minute (instantly queued).
    - **Authority**:
        - `Play`/`Like`/`Comment`: The actor (user performing the action).
        - **Comment Editing**: Users can edit their own comments. Peers see a `CommentEdit` operation that updates the text but preserves the original `CommentId`.
        - **Comment Deletion**: Users can delete their own comments via `CommentDelete`.
        - **Moderation**: Content owners (e.g., the artist who released the track) can remove (tombstone) comments from other peers on their own content. They cannot edit them.

### C. Social Interaction Manifest (Authority: User/Group)
Contains the user's social graph, profile, and community participation.
- **Operations**: `ProfileUpdate`, `Follow`, `Unfollow`, `FriendAdd`, `FriendRemove`, `GroupJoin`, `GroupLeave`, `GroupOperation`, `CompetitionOperation`.
- **Social Entities**:
    - **Groups**: Management (description, properties), Membership.
    - **Group Forums**: Channel management, threaded chats/posts.
    - **Competitions**: Creation, submissions, voting, results.
- **Characteristics**:
    - **Moderate Frequency**: Profile updates are rare; social graph changes occur during discovery.
    - **Snapshot-Friendly**: Most operations can be squashed into a "Current State" snapshot (e.g., current friend list).

## 2. Scalability and Compaction

### Multi-Tier Syncing
Peers do not need to sync all streams for all users:
- **Followed Artists**: Sync all three streams.
- **Discovered Peers**: Sync only the *Content Manifest* and *Social Manifest* (Profile).
- **Search Results**: Fetch only the *Content Manifest* metadata.

### Interaction Checkpoints
To prevent the *Content Interaction Manifest* from growing indefinitely:
1. **Play Count Aggregation**: Periodically, the user generates a `CheckpointSnapshot` that squashes individual plays into a compact table of `User -> Plays` for each track.
2. **Version Tracking**: Playcounts must hold an integer value for the `Version`. This allows displaying both total playcounts and playcounts for specific versions of a track.
3. **Consensus Protection**:
    - **Verification**: A playcount is only incremented when the user has played a certain percentage (e.g., 80%) of a track.
    - **Rate Limiting**: Peers enforce `SecurityLimits.MaxPlaysPerUserPerTrackPerDay`.
    - **Checkpoint Authentication**: Checkpoints must be consensus-authenticated. A peer will only accept a checkpoint if it aligns with the history of operations it has observed or if it meets network-wide consensus rules.

### Binary Serialization
Transitioning from JSON to a compact binary format (e.g., Protobuf or MessagePack) to reduce wire size by an estimated 70%.

## 3. Data and Bandwidth Estimations

### Per-User Data Projections (Year 1)
| Category | Ops/Year | Est. JSON Size | Est. Binary Size |
| :--- | :--- | :--- | :--- |
| **Content** | 200 | 200 KB | 40 KB |
| **Social** | 100 | 100 KB | 20 KB |
| **Interactions** | 5,000 | 2.5 MB | 500 KB |
| **Total** | **5,300** | **2.8 MB** | **560 KB** |

### Network Bandwidth Consumption
Assuming a peer follows 100 artists and interacts with 500 total peers. Daily updates include new plays, comments, and discovery metadata.

| Network Size | Daily Sync Vol (JSON) | Daily Sync Vol (Binary) | Peak Bandwidth (Binary) |
| :--- | :--- | :--- | :--- |
| **100 Users** | 15 MB / day | 3 MB / day | ~0.3 Kbps |
| **1,000 Users** | 150 MB / day | 30 MB / day | ~3 Kbps |
| **10,000 Users** | 1.5 GB / day | 300 MB / day | ~30 Kbps |

*Note: Distributed Search and Delta-Sync significantly reduce these numbers. Only "active" manifests are synced. In a 10k network, a user might only sync with a sub-mesh of 200-500 peers.*

## 4. Consensus and Integrity

### Play Count Spoofing
- **Rate Limiting**: (Implemented) `MaxPlaysPerUserPerTrackPerDay = 3`.
- **Proof of Play**: Future consideration — requiring a small PoW or a time-stamped "heartbeat" from the playback engine to validate long-duration plays.

### Like Spoofing
- **Identity-Bound**: One `Like` per `UserId` per `TargetId`.
- **History Verification**: Peers reject `Unlike` operations if no matching `Like` exists in the chain.

### Comment Integrity
- **Edit/Delete**: Comments include a `Version` or `LastEdited` timestamp.
- **Moderation**: Content owners can issue a `ContentModerationOp` that references a `CommentId` to signal to their followers that a comment should be hidden.

## 5. Implementation Roadmap

### Phase 1: Structural Split (Issue #127)
- Define `ManifestStreamType` enum.
- Update `ManifestManager` to support multiple local files per user (`{UserId}.content.json`, `{UserId}.social.json`, etc.).
- Update `ManifestExchangeProtocol` to allow requesting specific streams.

### Phase 2: Checkpointing and Consensus (Issue #128)
- Implement `InteractionCheckpoint` logic.
- Add support for squashing Play counts and Likes into signed snapshots.
- Implement playcount versioning (tracking plays per specific track content hash).
- Implement percentage-played verification in the playback engine.

### Phase 3: Content Authority and Moderation (Issue #129)
- Implement owner-based comment moderation (removing comments from other peers on own content).
- Add verification logic for track versioning and hash immutability.
- Implement "Set Verification" — ensuring the current state of a user's library is authentic without requiring the full history of changes.

### Phase 4: Binary Migration (Issue #32 - Existing)
- Move to Protobuf for all wire exchanges.
