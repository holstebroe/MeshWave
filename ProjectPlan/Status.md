# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (34/34, includes 8 PlayCountTests)
- Integration.Tests: passing (12/12)
- **Total: 82 tests passing**

## Active Sprint

Milestone D remainder -- Community Sync (IN PROGRESS)

Completed in latest session:
- Follow notifications badge on Community nav for followed artists with new releases
- Release feed now hydrates from followed peers' persisted Create operations
- Add to Library now requests peer content by hash and stores files in Other Music
- Community ingestion path now writes downloaded tracks into library structure with safe fallback
- Comment sync now uses signed manifest ops with ReplyToId threading and peer merge into playback timeline
- Social graph actions now publish signed FriendAdd/FriendRemove and GroupJoin/GroupLeave ops
- Likes sync now uses signed Like/Unlike ops and shows aggregate like counts in feed
- Bootstrap runtime split completed: `MeshWave.Bootstrap` console host + reusable `MeshWave.Bootstrap.Core` coordinator library
- NAT traversal pipeline expanded with ordered attempts (routing lookup -> bootstrap refresh -> direct TCP probe -> UDP punch -> bootstrap rendezvous session request -> content request -> concrete NAT guidance)
- Added detailed handshake documentation: `Documentation/P2P-Handshake.md`
- Started implementing crossing-hands plan: rendezvous request/response protocol + bootstrap session issuance + synchronizer fallback attempt integration
- Phase-2 implemented: bootstrap-provided coordinated probe window timing + synchronized rendezvous-window hole-punch attempt
- Added integration coverage for rendezvous scheduling and report visibility of rendezvous-window attempts

Milestone I -- Mesh Resilience and Background Mode (COMPLETE)
Milestone J -- Mesh Integration Tests (COMPLETE)
Milestone H -- Settings Storage and Housekeeping Tab (COMPLETE)

## Recently Completed

### Milestone J: Integration Tests
- Created MeshWave.Integration.Tests project (xUnit)
- NullPeerDiscovery stub for isolated test environments
- 10 integration tests now cover bootstrap discovery, coordinator registration, fixed-port bootstrap compatibility, operations signing, profile broadcast, follow/unfollow, merge events, signature verification, and connection-attempt NAT guidance fallback
- All tests passing
- Tests use dynamic ports and temp directories, no network conflicts

### Milestone I: Resilience and Background Mode
- PeerRouter periodic bootstrap re-contact (every 5 min, configurable in SecurityLimits)
- System tray icon (NotifyIcon) with context menu: Open MeshWave, Now Playing, Quit
- Window close -> hide to tray; one-time balloon notification informing user
- Explicit shutdown mode (OnExplicitShutdown) keeps app alive in background
- App.xaml and App.xaml.cs refactored for tray lifecycle
- MainWindow.xaml.cs OnClosing intercepts close events, hides to tray
- UseWindowsForms enabled in csproj; GlobalUsings alias file manages WPF/WinForms type conflicts
- Icon fallback to SystemIcons.Application if embedded resource invalid

### Milestone D remainder: Community Sync (latest)
- Follow notification badge now only lights for followed peers with new Create operations
- Community release feed now loads from persisted followed-peer manifests and refreshes on merge events
- Feed refresh now updates status text and empty states from real manifest data
- Release card '+ Library' now requests content by hash via SyncOrchestrator and stores into Other Music
- Add-to-library flow includes type-based extension resolution and raw-file fallback placement
- Playback comments now publish signed Comment ops and ingest peer Comment/CommentDelete ops per track
- Reply threading is preserved via replyToId metadata and rendered through timeline marker hierarchy
- Friends and group actions now publish signed social graph operations (FriendAdd/FriendRemove/GroupJoin/GroupLeave)
- Feed cards now support signed Like/Unlike operations with local toggle state and aggregate like counts

### Milestone H: Settings Storage and Housekeeping Tab
- Settings Storage tab now shows used/free/total disk space for selected drive
- Per-category storage breakdown implemented: My Music, Other Music, Manifests, Cache
- Progress bars use quota threshold color coding (green <70%, amber <90%, red >=90%)
- Clear peer manifest cache action clears both in-memory store and PeerManifests disk files
- Clear waveform cache action deletes cached *.waveform.json files under library folders
- Storage quota warning threshold is configurable and persisted in settings

### Milestone F: Artist and Fan Profiles (tail items)
- RecordFollow, RecordUnfollow: append signed Follow/Unfollow ops to manifest
- BroadcastProfile: signed Profile op with displayName, isArtist, bio, website, bannerImageHash
- CommunityViewModel: follow/unfollow command wiring, release badge, add-to-library stub
- ApplicationViewModel: forward community notifications to main badge
- MainWindow badge binding to HasCommunityNotification

## Architecture Decisions

- User-owned data principle: all user-generated content is propagated as signed manifest
  operations; no peer can forge another users data.
- Play count consensus: aggregate = sum of per-user counts; each users contribution is
  rate-capped by SecurityLimits.MaxPlaysPerUserPerTrackPerDay enforced during MergeManifest.
- Artist role: IsArtist is local preference + broadcast in signed Profile op; all peers
  are equal in P2P trust regardless of role.
- Background operation: app continues P2P mesh even when UI window is minimized to tray;
  bootstrap re-contact every 5 minutes ensures new users can join even if bootstrap was
  restarted. No centralized dependency once peers are connected.
- Community groups: fully distributed, no central server; GroupId derived from founding op
  hash + founder UserId. See Backlog Milestone G for full design.

## For Later

- Sybil-resistance / web-of-trust hardening for play count integrity
- Content exchange: TCP file transfer by content hash (Milestone D remainder)
- Community groups and distributed chat (Milestone G)
- Optional relay fallback mode after direct + rendezvous attempts fail
- Network diagnostics panel with per-attempt status and actionable NAT guidance details

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)

## Documentation Structure

This repository now uses:

- `Documentation/` for architecture and user-facing docs
- `ProjectPlan/` for implementation plans, backlog, and development status

## Active Focus

1. Profile persistence and icon generation
2. ? Play count sync — signed \Play\ ops in local manifest; session rate cap (one per track); \RecordPlay\ wired via \IsPlaying\ property change in ApplicationViewModel
3. ? Play count consensus — \MergeManifest\ enforces \MaxPlaysPerUserPerTrackPerDay=3\ per (trackId, utcDate) per user
4. Social graph model — Friends / Groups / Follows; comment permission policy (next focus)
5. Community mesh menu implemented (scaffold); next: wire real PeerManifestStore data into search/display
6. Per-peer manifest store complete
7. Bootstrap console node (MeshWave.Bootstrap) created

## Architecture Decisions

- **User-owned data principle:** all user-generated content (tracks, play counts, comments, likes, profile, chat) is propagated as *signed manifest operations*; no peer can forge another user's data.
- **Play count consensus:** aggregate = sum of per-user counts; each user's contribution is rate-capped by `SecurityLimits.MaxPlaysPerUserPerTrackPerDay` enforced during `MergeManifest`. See Backlog Architecture Notes for full design.

## For later
- Sybil-resistance / web-of-trust hardening for play count integrity
- Networking and P2P sync beyond manifest exchange (content/file transfer)


