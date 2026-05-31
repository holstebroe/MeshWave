# MeshWave Development Roadmap

## Current Priorities (ordered)

### 1) Community Browse + Shared Catalogue (TOP PRIORITY)
- Decide catalogue architecture (ADR):
  - Replicated metadata to all peers
  - Distributed query/search over mesh
  - Hybrid (local cache + selective distributed lookups)
- Define catalogue schema for artists, albums, tracks, playlists, availability, and peer sources
- Build catalogue ingestion/indexing pipeline and conflict/staleness rules
- Implement Browse UI with artist/album/track/playlist navigation and download actions
- Add play-while-downloading capability (buffer then start playback)
- Add pending downloads UI (queue, per-item progress, retry/error)

### 2) Search in Library + My Music
- Define local search behavior for track/album/artist/playlist fields
- Implement search in My Music (replace "coming soon")
- Implement search in Library (replace "coming soon")
- Add empty-result UX and clear-search behavior

### 3) Library download lifecycle
- Show pending downloads in Library views with progress indicators
- Remove-from-library keeps item visible in list as "Not Downloaded" state
- Define final wording/state model for non-local items

### 4) Transport fallback hardening
- Optional relay fallback (opt-in) only when direct + rendezvous fail
- Symmetric-NAT focused integration tests

## In Progress: Milestone D remainder (Community Sync)

### Done
- P2P foundation + signing + PeerRouter/PEX/bootstrap
- Per-peer manifest persistence
- Social/profile/feed/comment/likes baseline sync
- NAT traversal chain with rendezvous coordinated probe window
- Network diagnostics in Settings (attempt details + counters)

### Remaining
- Relay fallback (opt-in)
- Comment moderation sync (owner soft-delete)
- Comment permission enforcement

## Platform Expansion

### Done
- ARM Linux bootstrap RID baseline (`linux-arm`, `linux-arm64`)
- ARM publish helper script (`scripts/publish-bootstrap-arm.ps1`)

### Planned
- Mobile app (player-only): community playback + community interaction, recent-play cache policy
- Web frontend phase 1 (playback-focused), optional backend for user-owned file storage later

## Trust & Integrity (Milestone E)
- Sybil-resistance research spike
- Audit/replay verification for play-count integrity
- Per-user contribution-cap UI

## Community Groups (Milestone G)
- Group model + operation types (open/invite-only, admins, kick/ban, profile editing)
- Group manifest store + manager
- SyncOrchestrator group ops
- Groups UI: discover/join/request invite/channel/post/reply
- Admin panel: pending invites, member management, promote/demote moderators
- Group profile page: editable title, description, cover image, tags

## Future Feature Ideas

### Chat Channels in Groups
- Persistent text channels within a group, synced over the P2P manifest layer
- Threaded replies via ReplyToOpId; attachment support via content hash

### Music Competitions in Groups
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
