# MeshWave Development Roadmap

## Current Priorities (ordered)

### [1) Community Browse + Shared Catalogue (TOP PRIORITY)]
- [x] [Issue #17: Architecture decision](https://github.com/holstebroe/MeshWave/issues/17)
- [ ] [Issue #15: Catalogue sync pipeline](https://github.com/holstebroe/MeshWave/issues/15) (IN PROGRESS)
- [ ] [Issue #16: Play-while-downloading](https://github.com/holstebroe/MeshWave/issues/16) (IN PROGRESS)
- Decide catalogue architecture (ADR):
  - Replicated metadata to all peers
  - Distributed query/search over mesh
  - Hybrid (local cache + selective distributed lookups)
- Define catalogue schema for artists, albums, tracks, playlists, availability, and peer sources
- Build catalogue ingestion/indexing pipeline and conflict/staleness rules
- Implement Browse UI with artist/album/track/playlist navigation and download actions
- Add play-while-downloading capability (buffer then start playback)
- Add pending downloads UI (queue, per-item progress, retry/error)

### [2) Search in Library + Local Music](https://github.com/holstebroe/MeshWave/milestone/2)
- Define local search behavior for track/album/artist/playlist fields
- Implement search in Local Music (replace "coming soon")
- Implement search in Library (replace "coming soon")
- Add empty-result UX and clear-search behavior

### [3) Library download lifecycle](https://github.com/holstebroe/MeshWave/milestone/3)
- [ ] [Issue #119: [UI] Show pending downloads with progress in Library views](https://github.com/holstebroe/MeshWave/issues/119)
- [ ] [Issue #120: [Logic] Implement 'Not Downloaded' state for tracks removed from local storage](https://github.com/holstebroe/MeshWave/issues/120)
- [ ] [Issue #121: [UX] Standardize track state model and UI terminology](https://github.com/holstebroe/MeshWave/issues/121)
- Show pending downloads in Library views with progress indicators
- Remove-from-library keeps item visible in list as "Not Downloaded" state
- Define final wording/state model for non-local items

### 4) Transport fallback hardening
- [ ] [Issue #33: Optional relay fallback (opt-in)](https://github.com/holstebroe/MeshWave/issues/33)
- [ ] [Issue #58: NAT: Outbound-only manifest push via bootstrap](https://github.com/holstebroe/MeshWave/issues/58)
- [ ] [Issue #83: Network Health Indicator UI](https://github.com/holstebroe/MeshWave/issues/83)
- [ ] [Issue #84: Automated UPnP/NAT-PMP Mapping](https://github.com/holstebroe/MeshWave/issues/84)
- [ ] [Issue #85: Interactive NAT Troubleshooting Guide](https://github.com/holstebroe/MeshWave/issues/85)
- [ ] [Issue #124: [Settings] Implement Export Diagnostic Logs utility for troubleshooting](https://github.com/holstebroe/MeshWave/issues/124)
- Symmetric-NAT focused integration tests

## [In Progress: Milestone D remainder (Community Sync)](https://github.com/holstebroe/MeshWave/milestone/5)

### Done
- P2P foundation + signing + PeerRouter/PEX/bootstrap
- Per-peer manifest persistence
- Social/profile/feed/comment/likes baseline sync
- NAT traversal chain with rendezvous coordinated probe window
- Network diagnostics in Settings (attempt details + counters)

### Remaining
- [ ] [Issue #58: NAT: Outbound-only manifest push via bootstrap](https://github.com/holstebroe/MeshWave/issues/58) (IN PROGRESS)
- Relay fallback (opt-in)
- Comment moderation sync (owner soft-delete)
- Comment permission enforcement

## Platform Expansion

### Done
- ARM Linux bootstrap RID baseline (`linux-arm`, `linux-arm64`)
- ARM publish helper script (`scripts/publish-bootstrap-arm.ps1`)

### Planned
- **Mobile app (player-only)**:
  - [ ] [Issue #90: .NET MAUI project setup](https://github.com/holstebroe/MeshWave/issues/90)
  - [ ] [Issue #91: Recent-play cache policy](https://github.com/holstebroe/MeshWave/issues/91)
  - [ ] [Issue #92: Cross-platform player UI](https://github.com/holstebroe/MeshWave/issues/92)
- **Web frontend phase 1**:
  - [ ] [Issue #87: Baseline Blazor project](https://github.com/holstebroe/MeshWave/issues/87)
  - [ ] [Issue #88: Catalogue browser](https://github.com/holstebroe/MeshWave/issues/88)
  - [ ] [Issue #89: HTML5 Audio playback](https://github.com/holstebroe/MeshWave/issues/89)

## [Trust & Integrity (Milestone E)](https://github.com/holstebroe/MeshWave/milestone/6)
- Sybil-resistance research spike
- Audit/replay verification for play-count integrity
- Per-user contribution-cap UI

## Manifest Scalability and Performance (NEW)
- Implement **Delta Manifest Sync**: Move away from full manifest exchange to range-based requests to save bandwidth as history grows.
- Implement **Manifest Compaction**: Squash redundant operations (e.g., repeated profile updates or likes/unlikes) into state snapshots to keep manifests manageable.
- **Binary Wire Protocol**: Evaluate and implement a more compact binary serialization for manifests (Protobuf/MessagePack) to reduce network overhead.

## Community Groups (Milestone G)
- Group model + operation types (open/invite-only, admins, kick/ban, profile editing)
- Group manifest store + manager
- SyncOrchestrator group ops
- Groups UI: discover/join/request invite/channel/post/reply
- Admin panel: pending invites, member management, promote/demote moderators
- Group profile page: editable title, description, cover image, tags

## Future Feature Ideas

### [Track playback audio visualizer](https://github.com/holstebroe/MeshWave/milestone/17)
- [ ] [Issue #122: [Core] Implement AudioAnalysisService for real-time PCM and FFT data streaming](https://github.com/holstebroe/MeshWave/issues/122)
- [ ] [Issue #123: [UI] Create VisualizerWindow with OpenGL/Shader support](https://github.com/holstebroe/MeshWave/issues/123)

### Chat Channels in Groups
- [ ] [Issue #79: Channel and PostMessage Models](https://github.com/holstebroe/MeshWave/issues/79)
- [ ] [Issue #80: Threaded Messaging Logic](https://github.com/holstebroe/MeshWave/issues/80)
- [ ] [Issue #81: Chat Sidebar Navigation](https://github.com/holstebroe/MeshWave/issues/81)
- [ ] [Issue #82: Message Feed UI](https://github.com/holstebroe/MeshWave/issues/82)

### Music Competitions in Groups
- [ ] [Issue #74: Competition Models](https://github.com/holstebroe/MeshWave/issues/74)
- [ ] [Issue #75: Sealed Ballot Encryption](https://github.com/holstebroe/MeshWave/issues/75)
- [ ] [Issue #76: Validation and Deadlines](https://github.com/holstebroe/MeshWave/issues/76)
- [ ] [Issue #77: Competition Dashboard](https://github.com/holstebroe/MeshWave/issues/77)
- [ ] [Issue #78: Submission and Voting UI](https://github.com/holstebroe/MeshWave/issues/78)

A fully decentralised, sealed-ballot competition flow:

1. **Setup** — A group administrator creates a Competition op specifying: title, description,
   submission deadline, voting deadline, and whether votes are publicly revealed or
   admin-only until tally.
2. **Submission phase** — Group members submit tracks (up to the submission deadline) by
   appending a CompetitionSubmit op containing a content hash and optional description.
   At the submission deadline the playlist is locked (no new submissions accepted).
3. **Voting phase** — After the submission deadline and before the voting deadline, members
   cast votes by appending a CompetitionVote op. Votes are encrypted with the competition
   administrator's RSA public key so only the admin can decrypt the tally. This ensures
   votes remain secret until the admin chooses to publish results.
4. **Reveal** — When the voting deadline passes, the administrator decrypts the vote ops,
   tallies results, and appends a CompetitionResult op containing the ordered rankings and
   (optionally) each voter's choice. The result propagates over the mesh like any other op.
5. **Integrity** — Because all ops are signed and append-only, the vote history cannot be
   altered retroactively. Any peer can verify signatures and replay the manifest to confirm
   the tally is correct once votes are decrypted.

This requires additions to MeshWave.Common.Core:
- `CompetitionOperationType` enum: CreateCompetition | Submit | CastVote | PublishResult
- Sealed-vote encryption helper in CryptoService (RSA-OAEP encrypt/decrypt for vote payloads)
- Competition-aware merge rules in ManifestManager (deadline enforcement, lock check)

## Completed highlights
- Milestones A, B (baseline), F, H, I, J complete
- 83 tests currently passing (Common.Core + LibraryManager + Synchronizer + Integration)
