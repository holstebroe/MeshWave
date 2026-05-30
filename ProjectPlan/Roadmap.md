# MeshWave Development Roadmap

## Completed

- Core .NET 10 solution/project structure with dark theme (Spotify-inspired)
- Audio playback via NAudio; stable play/pause/stop/seek
- Settings persistence in AppData; base folder support
- My Music import with progress and cancellation
- Artist -> Album -> Track hierarchical library browsing
- Cover extraction/cache and display in library and playback view
- Waveform visualization: Filled, Cloudy, Mirror, Neon, Smooth styles; style persisted in settings
- Timeline comment markers with avatar icons; seek on click
- Waveform hover seek-preview overlay
- Profile editor: display name, avatar image, generated rounded icon
- P2P foundation: PeerDiscovery (UDP), ManifestExchangeServer/Client (TCP), SyncOrchestrator
- Manifest signing and verification using RSA; SecurityLimits constants
- P2PIdentityService; PeerRouter with LAN + bootstrap + PEX
- Per-peer manifest disk persistence (PeerManifestStore, 8 tests)
- Play count sync: signed Play ops, session rate cap, RecordPlay wired to IsPlaying
- Play count consensus: MergeManifest enforces MaxPlaysPerUserPerTrackPerDay=3
- Bootstrap console node (MeshWave.Bootstrap)
- Community view scaffold (CommunityViewModel + CommunityView)
- Dark-theme ComboBox style
- PathToBitmapConverter (OnLoad) to prevent file-lock on avatar images

## Active Sprint: Milestone I -- Mesh Resilience and Background Mode (COMPLETE)

### Goals
- Ensure the app continues P2P sync even when minimized to system tray
- Allow late-joining users to discover the network even if bootstrap nodes were restarted
- Provide a smooth UX when hiding to tray (one-time notification)
- Comprehensive integration tests to verify mesh stability

### Completed
1. PeerRouter periodic bootstrap re-contact (every 5 min, configurable)
2. System tray icon with context menu (Open, Now Playing, Quit)
3. Window close -> hide to tray with one-time notification
4. App continues running in background (OnExplicitShutdown mode)
5. MeshWave.Integration.Tests project (7 tests, all passing)

## Active Sprint: Milestone J -- Mesh Integration Tests (COMPLETE)

### Goals
Verify mesh stability protocol with in-process bootstrap node and multiple orchestrators.

### Completed
1. Bootstrap discovery test
2. Bootstrap retry interval configuration test
3. Fixed-port bootstrap compatibility test (39877 + custom peer ports)
4. Bootstrap coordinator library registration/PEX test
5. Connection-attempt NAT guidance fallback test
6. Signed operation verification test
7. Profile broadcast recording test
8. Follow/Unfollow operation recording test
9. ManifestMerged event test
10. Signature verification test
11. All 10 tests passing

## Milestone H -- Settings Storage and Housekeeping Tab (COMPLETE)

### Completed
1. Storage tab now shows used/free/total drive space
2. Per-category breakdown for My Music, Other Music, Manifests, and Cache
3. Progress bars color-coded by quota threshold (green/amber/red)
4. Clear cached peer manifests action wired to clear store and disk cache
5. Clear waveform cache action implemented
6. Configurable storage quota warning threshold persisted in settings

## Upcoming: Milestone G -- Community Groups and Distributed Chat

Groups are fully distributed -- identified by a group manifest, no central server.
Any peer can host and exchange group manifests like personal manifests.

Key design points:
- GroupId derived from hash of founding op + founder UserId (globally unique, no registration)
- Channels with threaded posts (ReplyToOpId)
- Soft-delete and ban ops for cooperative moderation
- Group discovery via PEX metadata broadcast
- Same ManifestExchangeServer/Client infrastructure reused

Example use cases: Roland Synth Junkies, Berlin Techno Producers, Ambient Drone Collective.

## Milestone D remainder (IN PROGRESS)

- [x] Community library ingestion (Other Music from peer manifests)
- [x] Comment sync via signed manifest ops (ReplyToId threading + peer merge into timeline)
- Comment moderation via manifest ops
- [x] Social graph sync (friends, follows, groups)
- [x] Likes sync
- [x] User profile sync (display name, avatar hash, IsArtist flag via signed Profile op)
- [x] Add to Library flow (content exchange)
- [x] Content exchange: direct TCP transfer by content hash with ordered attempts (routing lookup, bootstrap refresh, direct TCP probe, UDP hole-punch) and explicit NAT fallback guidance
- [ ] Bootstrap rendezvous ("crossing hands") session orchestration via bootstrap coordinator
- [ ] Coordinated simultaneous outbound probe window for peers behind restrictive NAT
- [ ] Optional relay fallback mode (bootstrap-assisted relay only when direct and rendezvous attempts fail)
- [ ] Network diagnostics surface in Settings with explicit user guidance details from fallback reports
- [x] Follow notifications badge on Community nav for followed artists with new Create ops
- [x] Release feed now hydrated from followed peers' persisted manifests (ordered newest-first)

## NAT Traversal Next Wave (planned under Milestone D remainder)

1. Bootstrap rendezvous token/session contract
2. Peer rendezvous state machine and timeout handling
3. Coordinated TCP/UDP simultaneous attempt scheduling
4. Relay fallback guardrails (bandwidth caps, opt-in, only-on-failure)
5. Integration tests for symmetric-NAT simulation paths

## Platform Expansion (planned)

- Define bootstrap build/publish configuration for ARM Linux (Raspberry Pi + lightweight cloud targets)
- Mobile player-only app: playback of community files + community interactions; cache only most-recently-played files
- Web frontend phase 1: playback-focused UI; future phase can add backend support for user-owned file storage

## Upcoming: Milestone E -- Trust and Aggregate Integrity

- Sybil-resistance research spike
- Audit log / replay verification for play count
- Per-user contribution cap UI

## Completed Milestones

### Milestone A: Core Playback
- NAudio integration with play/pause/stop/seek
- 5 waveform visualization styles (Filled, Cloudy, Mirror, Neon, Smooth)
- Timeline comment markers with hover preview

### Milestone B: Library Management
- File scanner (My Music / Other Music folders)
- Album/track hierarchical browsing
- Cover extraction and caching

### Milestone C: Persistence
- Settings saved to AppData (display name, waveform style, folder paths)

### Milestone D: Community Sync (partial)
- P2P foundation: UDP LAN discovery + TCP bootstrap + PEX
- RSA signing/verification with SecurityLimits protocol enforcement
- PeerRouter unified routing table (LAN + bootstrap + PEX)
- Manifest exchange and per-peer disk persistence
- Play count sync with session rate cap and daily consensus
- Bootstrap console node

### Milestone F: Artist and Fan Profiles
- IsArtist flag + extended fields (Bio, Website, BannerImagePath)
- 6-tab Settings layout (General | Profile | Artist | Appearance | Network | Storage)
- ReleasedAt timestamp on tracks/albums
- Release feed panel with RefreshFeedCommand
- Artist profile cards with ARTIST badge
- Follow/Unfollow signed manifest ops
- Profile broadcast (signed Profile op)

## Current Phase Summary

### Completed

- Core .NET 10 solution/project structure
- Dark theme styling baseline and readability fixes
- Audio playback via NAudio
- Stable stop/play behavior
- Shared playback session across tab navigation
- Icon-based playback controls
- Settings persistence in AppData
- Base folder support (`My Music`, `Other Music`)
- Editable/persisted supported extension list
- My Music import with progress + cancellation
- Artist -> Album -> Track hierarchical library browsing
- Separate Library (community) and My Music views
- Cover extraction/cache and display in library + playback
- Waveform cache loading; background generation when cache missing
- Timeline comment markers (first implementation)
- Scrollable artist/album/track lists

### In Progress

- Profile model/editing (name, avatar, generated icon)
- Play-count registration + sync preparation
- My Music release/version lifecycle (draft/released state and track update flow)
- Version notes and multi-version playback-ready data modeling (future mixes/compare flow)

## Next Milestones

### Milestone A: Profile + Social UX

- User profile persistence (name, avatar, bio)
- Rounded icon generation from profile image
- Use user icon in comments/timeline markers
- Friends / Groups / Follows social graph (local store first, P2P sync in Milestone D)
- Comment permission policy per album/track (`All` / `FriendsOnly` / `GroupsOnly` / `None`)
- Comment moderation: owner can delete comments on their material (soft-delete, propagated via P2P)

### Milestone B: Playback UX Completion

- Click marker/comment to seek
- Marker hover previews and richer visuals
- Stabilize waveform generation/caching telemetry
- Comment filtering by target track version (all/current toggle)
- Album/playlist side-panel tracklist browsing and in-view track switching

### Milestone C: Library & Persistence

- File-based DB or lightweight index for faster startup
- More robust artist/album statistics
- Improved search/filter and sorting

### Milestone D: Community Sync

- [x] P2P foundation: `PeerDiscovery` (UDP broadcast), `ManifestExchangeServer/Client` (TCP), `SyncOrchestrator`
- [x] Manifest signing + verification using RSA (`ManifestManager.AppendSignedOperation`, `VerifyManifest`, `MergeManifest`)
- [x] `SecurityLimits` — central constants enforced at TCP layer and manifest merge
- [x] `P2PIdentityService` — persistent RSA keypair, UserId derived from public key fingerprint
- [x] `PeerRouter` — unified routing table: LAN UDP + bootstrap nodes + PEX maintenance loop
- [x] PEX wire protocol (`GetPeers` request/response), capped at `SecurityLimits.MaxPeersPerExchange`
- [x] `AppSettings.P2PSettings.BootstrapNodes` — configurable internet bootstrap nodes
- [x] Wire `SyncOrchestrator` into `ApplicationViewModel` (auto-start when P2P enabled, graceful shutdown via `App.OnExit`)
- [ ] Per-peer manifest disk persistence
- [ ] Community library ingestion flow (`Other Music`) driven by peer manifests
- [ ] Play count sync — local signed manifest operations (`op: "play"`, rate-capped, one per session)
- [ ] Play count consensus — `MergeManifest` enforces `MaxPlaysPerUserPerTrackPerDay`; aggregate = sum of per-user capped counts
- [ ] Comment sync via manifest operations (signed, author-owned; includes `ReplyToId` threading)
- [ ] Comment moderation sync (owner soft-delete ops)
- [ ] Social graph sync (friends, groups, follows as signed user-owned manifest ops)
- [ ] Comment permission enforcement across peers (respect `CommentPolicy` from album manifest)
- [ ] Likes sync via manifest operations (one like per user per track, signed)
- [ ] User profile sync (display name, avatar hash broadcast as signed manifest operations)
- [ ] Content exchange: TCP file transfer by content hash

## Milestone E: Trust & Aggregate Integrity

- [ ] `SecurityLimits.MaxPlaysPerUserPerTrackPerDay` constant + enforcement in `MergeManifest`
- [ ] Sybil-resistance research spike (proof-of-work UserId registration or web-of-trust score)
- [ ] Audit log / replay verification for play count manifest operations
- [ ] Per-user contribution cap UI (show "X plays from Y unique listeners")
