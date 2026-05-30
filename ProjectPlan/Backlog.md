# MeshWave Backlog (High/Low Level)

## High-Level Feature Backlog

- [x] User profile editor (name, avatar) baseline in Settings
- [ ] User profile editor (bio and advanced profile fields)
- [ ] Rounded profile icon generation and use in timeline markers/comments
- [ ] Community sync for tracks/comments/play counts
- [ ] Play count registration threshold + sync
- [ ] Rich home dashboard polish/flashy visuals
- [ ] Home dashboard panel cards should navigate to respective tabs when clicked
- [ ] Persistent library index database (optional simple file DB first)

## Detailed TODO / Bugs

### Playback

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
- [ ] Album/playlist playback. The Playback page should have a panel with the current album/playlist tracklist, allowing users to easily see and select other tracks in the same album/playlist. This will require some UI redesign to accommodate the tracklist panel alongside the waveform and controls.

### Library (MyMusic)
- [x] Metadata editor baseline for Track (title, artist, album, year, genre, description)
- [ ] Metadata editor expansion for Artist/Album/Cover image management
- [ ] Single file import.


### Library (community)

- [x] Split Library (community) vs My Music
- [x] Artist->Album->Track filtering flow
- [x] Scrollable artist/album/track lists
- [x] Import progress + cancellation
- [x] Cover extraction and display
- [ ] Artist stats polish (albums/tracks/plays/comments from persisted stats)
- [ ] Community data loading model for Other Music

### Settings / Storage

- [x] Base folder selection and persistence
- [x] Supported extension list editable/persisted
- [x] Ensure `My Music` and `Other Music` folders
- [ ] Expanded settings (audio device, P2P settings UX)
- [ ] Improve contrast for secondary actions and popup/tooltips in dark theme (cancel/import/tooltips)

### Technical / Quality

- [ ] Replace Track.FileHash file-path workaround with dedicated FilePath field
- [ ] Add more automated tests for WPF viewmodels/services
- [ ] Better structured logging + error UI
- [ ] Performance pass for large libraries
- [ ] Move long-running import progress to popup-only UX (no inline panel)
- [ ] Clarify or remove non-functional search/filter text box until filtering is implemented
