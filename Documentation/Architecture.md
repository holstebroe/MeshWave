# MeshWave Architecture

## Solution Structure

MeshWave is organized into these projects:

- `MeshWave` (WPF app): UI, navigation, playback experience
- `MeshWave.LibraryManager`: import, indexing, metadata/cache management
- `MeshWave.Synchronizer`: P2P sync layer (in progress)
- `MeshWave.Common.Core`: shared domain models, crypto, storage abstractions

## Storage Model

### AppData (application state)

`%APPDATA%\MeshWave\`

- `settings.json` (persisted app settings)
- profile/settings-related files (planned expansion)

### Base folder (user-configurable)

`{BaseFolder}` (configured in Settings)

- `My Music/` (user-managed music)
- `Other Music/` (community-managed music)

Music is organized as:

`{Artist}/{Album or _singles_}/`

Inside each album folder:

- audio files
- `.cache/` (metadata cache, cover images, waveform cache)
- `.comments/` (track/album comment data)

## Current UI Architecture

- Shared playback session is managed centrally so playback continues while navigating tabs.
- Library UI is split into:
  - Community Library
  - My Music (with import workflow)
- Hierarchical browsing flow:
  - Artist -> Album -> Track
- Double-click track starts playback from both Library and My Music.

## Caching Strategy

- Metadata cache is read first.
- Re-scan is performed only when cache is missing or stale.
- Cover images are extracted and cached.
- Waveform data is loaded from cache when available.
- If waveform cache is missing, waveform is generated in the background during playback and then cached.

## In Progress

- Rounded user icons for timeline markers/comments
- User profile persistence and icon generation
- Community synchronization and play count sync
- Richer comment interaction (jump-to-marker, editing)
