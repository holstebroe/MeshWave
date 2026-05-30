# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (34/34, includes 8 PlayCountTests)

## Active Sprint

Milestone F -- Artist and Fan Profiles

Next immediate tasks:
1. Add IsArtist flag + extended fields (Bio, BannerImagePath, BannerImageHash, Website) to UserProfile
2. Restructure SettingsView into tabs (General | Profile | Artist | Appearance | Network | Storage)
3. Artist tab UI (bio, website, banner)
4. ReleasedAt timestamp on track/album sidecar
5. Release feed panel in CommunityView

## Recently Completed

- Play count sync: signed Play ops in local manifest, session rate cap (one per track per session)
- Play count consensus: MergeManifest enforces MaxPlaysPerUserPerTrackPerDay=3 per (trackId, utcDate)
- PeerManifestStore: per-peer manifest disk persistence, signature-verified
- Bootstrap console node (MeshWave.Bootstrap): PEX-only, --port/--seeds args
- Community view scaffold (CommunityViewModel + CommunityView with navigation)
- Dark-theme ComboBox style in SharedStyles
- PathToBitmapConverter: OnLoad bitmap loading prevents file-lock on avatar images
- Waveform hover seek-preview overlay (SeekPreviewOverlay on MouseMove/MouseLeave)

## Architecture Decisions

- User-owned data principle: all user-generated content is propagated as signed manifest
  operations; no peer can forge another users data.
- Play count consensus: aggregate = sum of per-user counts; each users contribution is
  rate-capped by SecurityLimits.MaxPlaysPerUserPerTrackPerDay enforced during MergeManifest.
- Artist role: IsArtist is local preference + broadcast in signed Profile op; all peers
  are equal in P2P trust regardless of role.
- Community groups: fully distributed, no central server; GroupId derived from founding op
  hash + founder UserId. See Backlog Milestone G for full design.

## For Later

- Sybil-resistance / web-of-trust hardening for play count integrity
- Content exchange: TCP file transfer by content hash (Milestone D remainder)
- Community groups and distributed chat (Milestone G)

## Build/Test

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


