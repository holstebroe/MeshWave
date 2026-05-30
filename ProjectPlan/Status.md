# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (34/34, includes 8 PlayCountTests)

## Active Sprint

Milestone F -- Artist and Fan Profiles (tail tasks)

Remaining tasks:
1. Add to Library flow (content exchange request ? Other Music folder)
2. Follow notifications badge (Community nav item, new Create ops since last sync)
3. Persist follow list as signed Follow manifest ops
4. User profile sync op (broadcast IsArtist, Bio, BannerImageHash, Website)

## Recently Completed

- ReleasedAt: DateTime? field on Track and Album models; AnnounceTrack/AnnounceAlbum stamp releasedAt in manifest op metadata
- Release feed panel: Feed tab added to CommunityView; ReleaseFeedItem model; RefreshFeedCommand; empty state
- Artist profile cards: Following tab shows full artist card (banner strip, avatar, ARTIST badge, bio, website, counts); Discover results show badge + bio snippet
- CommunityUserItem extended: IsArtist, Bio, Website, BannerImagePath fields
- ReleaseFeedItem model: ArtistDisplayName, Title, TargetType, ReleasedAt, ReleasedAtDisplay
- Settings tabbed layout (6 tabs: General | Profile | Artist | Appearance | Network | Storage)
- IsArtist flag + Bio, Website, BannerImagePath on UserProfile and User models
- Play count sync: signed Play ops, session rate cap, MergeManifest daily cap (3/user/track/day)
- PeerManifestStore, Bootstrap console node, Community view scaffold
- Dark-theme ComboBox, PathToBitmapConverter, waveform hover seek-preview, avatar file-lock fix

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


