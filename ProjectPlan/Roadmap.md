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

## Active Sprint: Milestone F -- Artist and Fan Profiles

### Goal
Differentiate artist and fan accounts. Give artists a richer profile (bio, banner image,
website). Restructure Settings into tabs to avoid clutter. Add a release feed so followers
can discover and quickly import new material.

### Tasks
1. Add IsArtist flag and extended fields to UserProfile model
2. Restructure SettingsView into tabs: General | Profile | Artist | Appearance | Network | Storage
3. Artist tab: Bio text box, Website field, Banner image picker and preview
4. Artist profile card component for CommunityView
5. ReleasedAt timestamp on track/album sidecar; set on AnnounceTrack/AnnounceAlbum
6. Release feed panel in CommunityView (recent Create ops from followed peers)
7. Add to Library one-click flow (stub -- full content exchange in Milestone D)
8. Follow notifications badge on Community nav item

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

See Backlog Milestone G for full implementation task list.

## Upcoming: Milestone H -- Settings Storage and Housekeeping Tab

- Storage tab in Settings: used/free disk space, per-category breakdown
- Visual progress bars with colour coding
- Clear cached manifests / clear waveform cache buttons

## Upcoming: Milestone D remainder

- Community library ingestion (Other Music from peer manifests)
- Comment sync and moderation via manifest ops
- Social graph sync (friends, follows, groups)
- Likes sync
- User profile sync op (broadcast Profile update as signed manifest op)
- Content exchange: TCP file transfer by content hash

## Upcoming: Milestone E -- Trust and Aggregate Integrity

- Sybil-resistance research spike
- Audit log / replay verification for play count
- Per-user contribution cap UI

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
