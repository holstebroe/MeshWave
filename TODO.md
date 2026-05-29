# MeshWave - TODO List

## 🐛 **CRITICAL BUGS - MUST FIX**

- [ ] **Dark Theme Text Visibility**: Black text titles very hard to see in dark theme
  - Update all TextBlock foreground colors to use `TextPrimaryColor` resource
  - Audit all views for readability issues
- [ ] **Playback Not Working After Stop**: After pressing Stop, Play button doesn't work
  - Fix audio service state management
  - Ensure file handle is not lost after Stop
- [ ] **Play/Pause Button UX**: Currently 3 separate buttons (Play/Pause/Stop)
  - Consolidate to single Play/Pause toggle button
  - Button icon/text should switch based on IsPlaying state
  - Keep Stop button separate (or remove if not needed)

---

## 🎯 **HIGH PRIORITY - Core Features**

### Settings & Storage Architecture
- [ ] **Settings Page Implementation**:
  - Choose base folder for all MeshWave data
  - Store settings in user's AppData folder (`%APPDATA%\MeshWave\settings.json`)
  - Audio output device selection
  - Theme customization
  - P2P settings (future)

- [ ] **Base Folder Structure**:
  ```
  {BaseFolder}/
  ├── My Music/           # User's own tracks
  │   └── {Artist}/
  │       └── {Album}/    # Or "_singles_" for singles without album
  │           ├── track1.mp3
  │           ├── track2.mp3
  │           ├── .cache/
  │           │   ├── track1_cover.jpg
  │           │   ├── track1_waveform.dat  # 1024-point downsampled waveform
  │           │   └── track1_meta.json     # Extracted metadata cache
  │           └── .comments/
  │               ├── track1_comments.json
  │               ├── track2_comments.json
  │               └── album_comments.json
  └── Other Music/        # Community/P2P tracks (read-only, app-managed)
      └── {Artist}/
          └── {Album}/
              └── ... (same structure)
  ```

- [ ] **Metadata Caching System**:
  - Scan files for metadata ONLY if:
    - File is new
    - File has changed (compare modification date or hash)
    - No cached metadata exists
  - Store metadata in `.cache/{trackname}_meta.json`
  - Fast startup: load from cache instead of re-scanning
  - Consider simple file-based database later (LiteDB, SQLite)

### User Profile System
- [ ] **User Profile**:
  - Name, picture/avatar, bio
  - Persist to `{AppData}\MeshWave\profile.json`
  - Generate small rounded corner icon from picture (for comments, artist view)
  - Profile editor UI in Settings

- [ ] **Profile Icon Generation**:
  - Crop and resize user picture to small icon (e.g., 48x48)
  - Apply rounded corners
  - Display next to user's comments
  - Display in artist list for local user

### Library Organization Overhaul
- [ ] **Artist → Album → Track Hierarchy**:
  - Library view should organize by Artist first
  - Then show Albums under each artist (including "_singles_")
  - Use cached metadata for fast loading
  - Show artist profile icons

- [ ] **Artist List View**:
  - Show profile picture/icon for each artist
  - Display statistics:
    - Number of albums
    - Number of tracks
    - Total play count
    - Total comments
  - Expandable/collapsible artist sections

- [ ] **Album Organization**:
  - Group tracks by album (from metadata)
  - Special "_singles_" album for tracks without album tag
  - Show album cover thumbnail
  - Album-level comments

- [ ] **Import My Music Button**:
  - Replace "Select Music Folder" with "Import My Music"
  - Copy files to `{BaseFolder}/My Music/{Artist}/{Album}/`
  - Organize by artist and album automatically
  - Extract and cache metadata during import
  - Generate waveform cache during import

### Playback View Enhancements
- [ ] **Real Waveform Display**:
  - Load cached waveform from `.cache/{trackname}_waveform.dat`
  - 1024-point downsampled amplitude data
  - Generate during import if not cached
  - Smooth rendering with actual audio peaks

- [ ] **Comment Markers on Timeline**:
  - Display small user icons on waveform timeline where comments exist
  - Tooltip shows comment preview on hover
  - Click marker to seek to comment timestamp
  - Visual indication of comment density

### Play Count & Statistics
- [ ] **Play Count Tracking**:
  - Register play count when track played > X% (e.g., 50% or 80%)
  - Store in metadata cache or separate stats file
  - Display play count in library view
  - Sync play counts with community (P2P)

### Comment System Improvements
- [ ] **Comment File Storage**:
  - Store comments in `.comments/{trackname}_comments.json`
  - Album-level comments in `.comments/album_comments.json`
  - Stable format for P2P synchronization
  - Include user profile icon in comment data

---

## 🔧 **MEDIUM PRIORITY**

### Playback Enhancements
- [ ] **Real Waveform Generation**: Generate actual waveform from audio data using NAudio sample data
  - Use peak detection and downsampling for performance
  - Store waveform data per track for reuse
- [ ] **Album Cover Display**: Extract cover art from ID3 tags (TagLib# supports this)
  - Display in playback view
  - Show thumbnails in library view
- [ ] **Click Comment to Seek**: Make comments clickable to jump to timestamp
- [ ] **Delete Comments**: Add delete button for each comment
- [ ] **Keyboard Shortcuts**:
  - Space: Play/Pause
  - Left/Right Arrow: Seek ±5s
  - Up/Down Arrow: Volume ±10%
  - M: Mute/Unmute

### Library Improvements
- [ ] **Search/Filter**: Add search box to filter tracks/albums by name
- [ ] **Persist Library Index**: Save to SQLite/LiteDB to avoid re-scanning on startup
- [ ] **Compute File Hashes**: SHA-256 for content addressing (currently placeholder)
- [ ] **Album Cover Thumbnails**: Show in library list
- [ ] **Sort Options**: Sort by title, artist, album, date added
- [ ] **Track Details View**: Show full metadata (bitrate, sample rate, file size, path)

### User Experience
- [ ] **Drag-and-Drop Import**: Drag files/folders into library view
- [ ] **Setup Wizard**: First-run experience
  - Choose base folder
  - Set display name and profile picture
  - Generate keypair (future)
- [ ] **Error Handling**: Show user-friendly messages for:
  - File not found
  - Unsupported format
  - Playback errors

---

## 🔧 **LOWER PRIORITY**

### Playlist Features
- [ ] **Create Playlists**: New playlist UI
- [ ] **Add Tracks to Playlist**: Drag or right-click context menu
- [ ] **Save/Load Playlists**: Persist to disk (JSON or M3U format)
- [ ] **Playlist Navigation**: Next/Previous track buttons
- [ ] **Shuffle & Repeat**: Playback modes

### Advanced Playback
- [ ] **Equalizer**: Basic EQ with presets
- [ ] **Playback Speed Control**: 0.5x to 2x speed
- [ ] **Crossfade**: Smooth transitions between tracks
- [ ] **Gapless Playback**: For albums/playlists
- [ ] **Audio Visualizer**: Spectrum analyzer or oscilloscope

### Community Features (P2P Integration)
- [ ] **Community Library View**: Browse music from connected peers
- [ ] **Selective Sync**: Choose which users/albums/tracks to sync
- [ ] **Sync Status UI**: Show download progress, connected peers
- [ ] **Notifications**: New content available, sync complete
- [ ] **User Profile Page**: Display name, bio, avatar, public key

---

## 🚀 Low Priority / Future

### Advanced Features
- [ ] **Lyrics Display**: Show synced lyrics (if available in tags)
- [ ] **Smart Playlists**: Auto-generate based on criteria (genre, date, rating)
- [ ] **Track Rating**: 5-star rating system
- [ ] **Play Count**: Track how many times each song is played
- [ ] **Recently Played**: History view
- [ ] **Favorites**: Quick access to favorite tracks
- [ ] **Mini Player Mode**: Compact always-on-top window

### Metadata Management
- [ ] **Edit Track Metadata**: Title, artist, album, genre, year, etc.
- [ ] **Batch Edit**: Update multiple tracks at once
- [ ] **Fetch Metadata Online**: MusicBrainz, Last.fm integration
- [ ] **Automatic Tagging**: Fix missing/incorrect metadata
- [ ] **Album Art Editor**: Crop, resize, replace covers

### Export/Import
- [ ] **Export Library**: CSV, JSON, XML
- [ ] **Import from iTunes/Spotify**: Parse library files
- [ ] **Export Playlists**: M3U, M3U8, PLS formats
- [ ] **Backup Settings**: Export/import app settings

### Performance Optimizations
- [ ] **Lazy Loading**: Load library in chunks for large collections
- [ ] **Background Indexing**: Index without blocking UI
- [ ] **Waveform Caching**: Pre-generate and cache waveforms
- [ ] **Memory Management**: Optimize large library handling

---

## 🐛 Known Issues

- [x] **Stack Overflow on Double-Click Track**: Fixed by preventing circular CurrentPosition updates
- [ ] **Waveform is Placeholder**: Not generated from actual audio data
- [ ] **File Path in FileHash**: Need proper `FilePath` property in `Track` model
- [ ] **No Error Handling**: App crashes on missing files or unsupported formats
- [ ] **Multiple Refreshes on Bulk Operations**: File watcher triggers multiple times

---

## 📝 Technical Debt

- [ ] **Add `FilePath` Property to Track Model**: Currently using `FileHash` as workaround
- [ ] **Separate UI and Business Logic**: Move more logic out of code-behind
- [ ] **Add Unit Tests**: ViewModels, services, library manager
- [ ] **Add Integration Tests**: Full playback workflow
- [ ] **Improve Error Logging**: Add structured logging (Serilog?)
- [ ] **Async/Await**: Make library indexing and file operations async
- [ ] **Dependency Injection**: Use DI container for services
- [ ] **Configuration System**: appsettings.json for settings

---

## 🎨 UI/UX Improvements

- [ ] **Loading Indicators**: Show progress during library scan
- [ ] **Empty State Messages**: Friendly messages when no tracks/albums
- [ ] **Tooltips**: Helpful tooltips for buttons and controls
- [ ] **Animations**: Smooth transitions and fade effects
- [ ] **Responsive Layout**: Better handling of window resizing
- [ ] **Context Menus**: Right-click on tracks for options
- [ ] **Status Bar**: Show current status, track count, playback info
- [ ] **Breadcrumbs**: Navigation trail (e.g., Library > Albums > Rock)

---

## 🔐 Security & Privacy

- [ ] **Keypair Generation**: RSA/Ed25519 for user identity
- [ ] **Manifest Signing**: Sign all manifests with private key
- [ ] **Signature Verification**: Verify signatures before accepting content
- [ ] **Encrypted Transfers**: TLS for P2P transfers
- [ ] **Privacy Settings**: Control what is shared with peers
- [ ] **Key Backup/Export**: Allow users to backup their private key

---

## 🌐 P2P Networking (Future)

- [ ] **Peer Discovery**: mDNS for LAN, bootstrap nodes for internet
- [ ] **NAT Traversal**: STUN/TURN for connectivity
- [ ] **Manifest Exchange**: Sync manifests with peers
- [ ] **Content Transfer**: Request/send audio files by hash
- [ ] **Resumable Downloads**: Resume interrupted transfers
- [ ] **Bandwidth Throttling**: Limit upload/download speed
- [ ] **DHT**: Distributed hash table for content discovery

---

**Last Updated**: 2025-05-29  
**Status**: Core playback and library features implemented. Ready for testing and iterative improvements.
