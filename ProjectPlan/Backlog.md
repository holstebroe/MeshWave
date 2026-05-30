# MeshWave Backlog (High/Low Level)

## High-Level Feature Backlog

- [x] User profile editor (name, avatar) baseline in Settings
- [ ] User profile editor (bio and advanced profile fields)
- [x] Rounded profile icon generation and use in timeline markers/comments
- [x] Profile/setup page shows avatar preview and generated icon preview
- [x] Community sync for tracks/comments/play counts (P2P foundation: UDP discovery, manifest exchange server/client, SyncOrchestrator)
- [ ] Play count registration threshold + sync
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
- [x] Album/playlist playback. The Playback page should have a panel with the current album/playlist tracklist, allowing users to easily see and select other tracks in the same album/playlist. This will require some UI redesign to accommodate the tracklist panel alongside the waveform and controls.

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
- [ ] P2P: per-peer manifest store (disk persistence, one manifest file per peer UserId)
- [x] P2P: SecurityLimits enforcement (message size, field lengths, manifest op count, routing table cap)
- [x] P2P: P2PIdentityService (persistent RSA keypair in AppData)
- [x] P2P: PeerRouter (LAN UDP + bootstrap nodes + PEX maintenance loop)
- [x] P2P: PEX GetPeers protocol (server + client, rate-capped)
- [x] P2P: BootstrapNodes config added to AppSettings.P2PSettings
- [ ] P2P: content exchange (request/serve audio files by content hash over TCP)
- [ ] P2P: display discovered peers and their released tracks in the Community Library view
- [ ] P2P: play count sync (record local counts, broadcast as manifest operations)
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
- [x] Clarify or remove non-functional search/filter text box until filtering is implemented
- [x] Ensure cover cache writes standardized .jpg file output in LocalLibraryManager
- [x] Remove waveform bar gap artifacts in playback waveform rendering
