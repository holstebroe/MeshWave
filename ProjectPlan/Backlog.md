# MeshWave Backlog (High/Low Level)

## High-Level Feature Backlog

- [ ] User profile editor (name, avatar, bio)
- [ ] Rounded profile icon generation and use in timeline markers/comments
- [ ] Community sync for tracks/comments/play counts
- [ ] Play count registration threshold + sync
- [ ] Rich home dashboard polish/flashy visuals
- [ ] Persistent library index database (optional simple file DB first)

## Detailed TODO / Bugs

### Playback

- [x] Stop->Play reliability fixed
- [x] Avoid multiple simultaneous tracks when selecting new track
- [x] Keep playback active while switching tabs
- [x] Preserve waveform/cursor behavior when returning to playback tab
- [x] Replace fake waveform with real waveform generation pipeline
- [x] Background waveform generation when cache is missing
- [ ] Marker click seeks playback to timestamp
- [ ] Marker tooltip/avatar polish

### Library / My Music

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

### Technical / Quality

- [ ] Replace Track.FileHash file-path workaround with dedicated FilePath field
- [ ] Add more automated tests for WPF viewmodels/services
- [ ] Better structured logging + error UI
- [ ] Performance pass for large libraries
