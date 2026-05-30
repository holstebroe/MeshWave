# MeshWave Development Roadmap

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
