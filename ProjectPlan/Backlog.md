# MeshWave Backlog (High/Low Level)

## High-Level Feature Backlog

- [x] User profile editor (name, avatar) baseline in Settings
- [ ] User profile editor (bio and advanced profile fields)
- [x] Rounded profile icon generation and use in timeline markers/comments
- [x] Profile/setup page shows avatar preview and generated icon preview
- [x] Community sync for tracks/comments/play counts (P2P foundation: UDP discovery, manifest exchange server/client, SyncOrchestrator)
- [ ] Play count registration threshold + sync
- [ ] Social graph: Friends, Groups, Follows (see Social section)
- [ ] Comment permissions per album/track (All / Groups / Friends only)
- [ ] Comment moderation: track owners can delete comments on their own material
- [ ] Rich home dashboard polish/flashy visuals
- [x] Home dashboard panel hover style polish (discrete inner border highlight, no ugly outer hover)
- [x] Home dashboard panel cards should navigate to respective tabs when clicked
- [ ] Persistent library index database (optional simple file DB first)

## Detailed TODO / Bugs

### Playback

- [x] Comment filter options by current track version (show all vs current only)
- [x] Persist track version on timeline comments for future filtering
- [x] Stop->Play reliability fixed
- [x] Avoid multiple simultaneous tracks when selecting new track
- [x] Keep playback active while switching tabs
- [x] Preserve waveform/cursor behavior when returning to playback tab
- [x] Replace fake waveform with real waveform generation pipeline
- [x] Background waveform generation when cache is missing
- [x] Marker click seeks playback to timestamp
- [x] Marker tooltip/avatar polish
- [x] Play / pause button simplified to a single Play/Pause toggle (icons, no text).
- [x] Show song description if available.
- [x] Album/playlist playback. The Playback page should have a panel with the current album/playlist tracklist, allowing users to easily see and select other tracks in the same album/playlist.
- [x] Comments follow the playing track (bug: comments were not cleared on track change)
- [x] Comment timestamp captured at first keystroke, not at submit time
- [x] Ctrl+Enter submits comment
- [x] Comment data model has `Id` + `ReplyToId` for threaded replies; replies shown indented under parent
- [ ] Comment reply UI: reply button per comment, inline reply box
- [ ] Comment delete: track owners can delete comments on their own tracks
- [ ] Comment like / reaction

### Social

- [ ] Friends: add/remove friends (by UserId / display name); bilateral relationship stored in user profile manifest
- [ ] Groups: create/manage groups; group membership list stored in user-owned group manifest
- [ ] Follows: follow any user (unilateral); stored in follower's manifest
- [ ] Social graph sync via P2P manifest operations (signed, owner-only writes)
- [ ] Comment permission model per album/track: `CommentPolicy` enum (`All` | `FriendsOnly` | `GroupsOnly` | `None`)
  - Stored in album/track metadata sidecar
  - Enforced locally when adding comments; broadcast in album manifest so peers respect it
- [ ] Comment moderation: owner can mark a comment as deleted (soft-delete via signed manifest op; propagates to peers)

### Library (MyMusic)
- [x] Metadata editor baseline for Track (title, artist, album, year, genre, description)
- [x] Metadata editor expansion for Artist/Album/Cover image management
- [x] Single file import.
- [x] Normalize My Music import button sizing/alignment
- [x] Release/unrelease toggle and version field in My Music metadata editor (album + track)
- [x] Show release status/version badges in My Music album and track lists
- [x] Baseline API for updating imported track file while preserving metadata sidecars
- [ ] Track version change notes (what changed) and structure for future multi-version mix browsing/playback


### Library (community)

- [x] Split Library (community) vs My Music
- [x] Artist->Album->Track filtering flow
- [x] Scrollable artist/album/track lists
- [x] Import progress + cancellation
- [x] Cover extraction and display
- [ ] Artist stats polish (albums/tracks/plays/comments from persisted stats)
- [ ] Community data loading model for Other Music
- [x] P2P: integrate SyncOrchestrator into ApplicationViewModel (start/stop on app launch)
- [x] P2P: per-peer manifest store (disk persistence, one manifest file per peer UserId)
- [x] P2P: SecurityLimits enforcement (message size, field lengths, manifest op count, routing table cap)
- [x] P2P: P2PIdentityService (persistent RSA keypair in AppData)
- [x] P2P: PeerRouter (LAN UDP + bootstrap nodes + PEX maintenance loop)
- [x] P2P: PEX GetPeers protocol (server + client, rate-capped)
- [x] P2P: BootstrapNodes config added to AppSettings.P2PSettings
- [ ] P2P: content exchange (request/serve audio files by content hash over TCP)
- [x] Community mesh menu (search users/groups, follow, add friend, join group) — CommunityView + CommunityViewModel scaffold wired into navigation
- [x] Bootstrap console application (MeshWave.Bootstrap) — minimal PEX-only node; no manifest data stored or served; configurable port + seed list
- [ ] Community view: wire real peer data into search results from PeerManifestStore / PeerRouter
- [ ] Community view: persist social graph (friends/follows/groups) as manifest operations
- [ ] P2P: likes sync (push/pull as manifest operations; one like per user per track, signed)
- [ ] P2P: user profile sync (broadcast own profile — display name, avatar hash — as manifest operations; only owner can update)
- [ ] P2P: play count sync (record local counts, broadcast as manifest operations)
- [ ] P2P: play count consensus — aggregate across peers, prevent single-user manipulation (see Architecture note below)
- [ ] P2P: comment sync (push/pull timeline comments as manifest operations)

### Settings / Storage

- [x] Base folder selection and persistence
- [x] Supported extension list editable/persisted
- [x] Ensure `My Music` and `Other Music` folders
- [ ] Expanded settings (audio device, P2P settings UX)
- [ ] Improve contrast for secondary actions and popup/tooltips in dark theme (cancel/import/tooltips)

### Technical / Quality

- [x] Replace Track.FileHash file-path workaround with dedicated FilePath field
- [ ] Add more automated tests for WPF viewmodels/services
- [ ] Better structured logging + error UI
- [ ] Performance pass for large libraries
- [x] Move long-running import progress to popup-only UX (no inline panel)

---

## Architecture Notes

### User-Owned Data Model

All data generated by a user is owned exclusively by that user and propagated through the P2P network
as **signed manifest operations**. Because every operation is signed with the user's RSA private key
(see `P2PIdentityService`, `ManifestManager.AppendSignedOperation`), no other peer can forge or
modify another user's data. Categories of user-owned data:

| Category | Examples | Authority |
|---|---|---|
| Music tracks + metadata | Title, artist, album, description, release status, version | Track owner only |
| Play counts | Per-user per-track play count increments | Counted user only |
| Comments & likes | Timeline comments, track likes | Comment/like author only |
| User profile | Display name, avatar image hash, bio | Profile owner only |
| Chat messages *(future)* | Direct or room messages | Message sender only |

### Play Count Consensus

**Goal:** Display an aggregate play count across all peers that cannot be inflated by a single user.

**Design (to implement in Milestone D / E):**

1. **Local recording** — Each user stores their own play count increments locally in the track
   metadata sidecar (`.mymusic.json`). Each "play" is a discrete signed manifest operation:
   `{ op: "play", trackId, userId, timestamp, sequenceNumber }`.

2. **One-play-per-session rule** — A play is registered only once per continuous listening session
   (e.g., ≥ 30 seconds listened, same track, same session). The client enforces this before
   appending the manifest operation.

3. **Rate limiting at merge** — `ManifestManager.MergeManifest` enforces
   `SecurityLimits.MaxPlaysPerUserPerTrackPerDay` (to be added). Any peer broadcasting more play
   increments than this threshold in a single manifest sync is clamped/ignored, protecting the
   aggregate from spam.

4. **Aggregate display** — The displayed play count for a track is the **sum of unique-user play
   counts** collected from all peer manifests received so far. Each user contributes at most their
   own daily-capped count. No single peer can raise the global aggregate beyond their own capped
   contribution.

5. **Future hardening** — A Sybil-resistance layer (e.g., proof-of-work on UserId registration or
   a web-of-trust score) may be added later if fake account farming becomes a concern. Tracked in
   the roadmap under Milestone E.

**Security boundary:** A user can only influence their *own* row in the distributed play-count
table, bounded by the daily cap. The global aggregate is tamper-evident because every contributing
operation is individually signed and sequence-checked.
- [x] Clarify or remove non-functional search/filter text box until filtering is implemented
- [x] Ensure cover cache writes standardized .jpg file output in LocalLibraryManager
- [x] Remove waveform bar gap artifacts in playback waveform rendering
