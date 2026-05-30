# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (34/34, includes 8 PlayCountTests)
- Integration.Tests: passing (7/7)
- **Total: 77 tests passing**

## Active Sprint

Milestone I -- Mesh Resilience and Background Mode (COMPLETE)
Milestone J -- Mesh Integration Tests (COMPLETE)

Upcoming: Milestone H -- Settings Storage and Housekeeping Tab

## Recently Completed

### Milestone J: Integration Tests
- Created MeshWave.Integration.Tests project (xUnit)
- NullPeerDiscovery stub for isolated test environments
- 7 integration tests: Bootstrap discovery, operations signing, profile broadcast, follow/unfollow, merge events, signature verification
- All tests passing in 6.1 seconds
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
- Add to Library flow (triggers content exchange, places files in Other Music)
- Follow notifications badge (when followed artist has new releases)

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


