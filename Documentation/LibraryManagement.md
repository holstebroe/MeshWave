# MeshWave — Library Management Design

## Library Folders
- **Local Music**: User's personal music files. Can be updated by adding/removing files in the folder. Indexed and scanned for metadata by the app.
- **Community Music**: Files received from the MeshWave network. Managed and updated only by the app (not directly by the user).

## Scanning and Indexing
- When the user selects their music folder, MeshWave scans for supported audio files (mp3, flac, wav, etc.).
- Metadata (title, album, artist, cover, duration) is extracted using TagLib#.
- Tracks and albums are indexed and shown in the UI.
- Community music is updated only by the synchronizer and not by direct file changes.

## Planned Features
- File watchers for incremental updates in Local Music.
- Drag-and-drop import for Local Music.
- Community music sync and update via P2P only.

## Implementation Notes
- Track and album lists in the UI are updated after scanning.
- Only Local Music can be updated by the user directly.
- Community music is read-only from the user's perspective.
