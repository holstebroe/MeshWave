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
- Group model + operation types
- Group manifest store + manager
- SyncOrchestrator group ops
- Groups UI (discover/join/channel/post/reply)

## Completed highlights
- Milestones A, B (baseline), F, H, I, J complete
- 83 tests currently passing (Common.Core + LibraryManager + Synchronizer + Integration)
