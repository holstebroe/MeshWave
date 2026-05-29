# MeshWave - Storage Architecture & Design

## 📁 **Folder Structure**

### Overview
MeshWave uses a centralized base folder for all data, with separate areas for user's music and community-shared music.

```
{BaseFolder}/                          # User-configurable, default: %USERPROFILE%\Music\MeshWave
│
├── My Music/                          # User's personal music library
│   ├── {Artist1}/
│   │   ├── {Album1}/
│   │   │   ├── track1.mp3
│   │   │   ├── track2.mp3
│   │   │   ├── track3.mp3
│   │   │   ├── .cache/                # Metadata and derived data
│   │   │   │   ├── track1_cover.jpg
│   │   │   │   ├── track1_waveform.dat
│   │   │   │   ├── track1_meta.json
│   │   │   │   ├── track2_cover.jpg
│   │   │   │   ├── track2_waveform.dat
│   │   │   │   ├── track2_meta.json
│   │   │   │   ├── album_cover.jpg     # Album-wide cover
│   │   │   │   └── album_meta.json     # Album metadata
│   │   │   └── .comments/              # Comment data
│   │   │       ├── track1_comments.json
│   │   │       ├── track2_comments.json
│   │   │       └── album_comments.json  # Album-level comments
│   │   │
│   │   ├── {Album2}/
│   │   │   └── ... (same structure)
│   │   │
│   │   └── _singles_/                  # Tracks without album
│   │       ├── single1.mp3
│   │       ├── single2.mp3
│   │       ├── .cache/
│   │       │   ├── single1_cover.jpg
│   │       │   ├── single1_waveform.dat
│   │       │   └── single1_meta.json
│   │       └── .comments/
│   │           └── single1_comments.json
│   │
│   └── {Artist2}/
│       └── ... (same structure)
│
└── Other Music/                       # Community/P2P music (read-only)
	├── {PeerUser1}/
	│   └── {Album}/
	│       └── ... (same structure as My Music)
	│
	└── {PeerUser2}/
		└── ... (same structure)
```

### AppData Folder (Application Settings)
```
%APPDATA%\MeshWave\                    # Windows: C:\Users\{User}\AppData\Roaming\MeshWave
├── settings.json                      # Application settings
├── profile.json                       # User profile (name, bio, avatar)
├── user_icon.png                      # Generated rounded profile icon (48x48)
├── keypair.dat                        # User's cryptographic keypair (encrypted)
├── library_cache.db                   # Fast-loading library index (LiteDB/SQLite)
└── logs/                              # Application logs
	├── app_2025-05-29.log
	└── error_2025-05-29.log
```

---

## 🗂️ **File Format Specifications**

### Track Metadata Cache (`track_meta.json`)
```json
{
  "trackId": "hash-or-guid",
  "title": "Song Title",
  "artist": "Artist Name",
  "album": "Album Name",
  "duration": "00:03:45",
  "fileHash": "sha256-hash-of-file",
  "fileSize": 5242880,
  "bitrate": 320,
  "sampleRate": 44100,
  "fileFormat": "MP3",
  "lastModified": "2025-05-29T12:34:56Z",
  "cachedAt": "2025-05-29T12:35:00Z",
  "playCount": 15,
  "lastPlayed": "2025-05-29T10:00:00Z",
  "tags": ["Rock", "Alternative"],
  "coverHash": "sha256-hash-of-cover"
}
```

### Album Metadata Cache (`album_meta.json`)
```json
{
  "albumId": "hash-or-guid",
  "title": "Album Title",
  "artist": "Artist Name",
  "year": 2024,
  "trackCount": 12,
  "totalDuration": "00:45:30",
  "genre": "Rock",
  "coverHash": "sha256-hash-of-cover",
  "cachedAt": "2025-05-29T12:35:00Z"
}
```

### Waveform Data (`track_waveform.dat`)
Binary format: 1024 float values representing downsampled peak amplitudes.

**Format**:
- Header (16 bytes):
  - Magic number: `MWWF` (4 bytes)
  - Version: 1 (4 bytes)
  - Sample count: 1024 (4 bytes)
  - Reserved: 0 (4 bytes)
- Data (4096 bytes):
  - 1024 × 4-byte floats (range: -1.0 to 1.0)

**Generation**:
1. Load audio file with NAudio
2. Divide into 1024 time buckets
3. Calculate peak amplitude (min/max) for each bucket
4. Store as normalized float (-1.0 to 1.0)

### Comment Data (`track_comments.json`)
```json
{
  "trackId": "hash-or-guid",
  "comments": [
	{
	  "commentId": "uuid",
	  "authorUserId": "user-id-or-public-key-hash",
	  "authorName": "Username",
	  "authorIconHash": "sha256-of-icon",
	  "timestamp": 125.5,
	  "text": "Great solo here!",
	  "createdAt": "2025-05-29T10:00:00Z",
	  "signature": "rsa-signature"
	}
  ]
}
```

### Album Comments (`album_comments.json`)
```json
{
  "albumId": "hash-or-guid",
  "comments": [
	{
	  "commentId": "uuid",
	  "authorUserId": "user-id",
	  "authorName": "Username",
	  "authorIconHash": "sha256-of-icon",
	  "text": "Amazing album!",
	  "createdAt": "2025-05-29T10:00:00Z",
	  "signature": "rsa-signature"
	}
  ]
}
```

### Application Settings (`settings.json`)
```json
{
  "version": "1.0",
  "baseFolder": "C:\\Users\\Me\\Music\\MeshWave",
  "theme": "Dark",
  "audioDevice": "Default",
  "p2p": {
	"enabled": false,
	"port": 47474,
	"maxPeers": 10,
	"uploadLimit": 0,
	"downloadLimit": 0
  },
  "playback": {
	"registerPlayAt": 0.5,
	"volume": 0.8,
	"crossfadeDuration": 2
  }
}
```

### User Profile (`profile.json`)
```json
{
  "userId": "hash-of-public-key",
  "displayName": "John Doe",
  "bio": "Music enthusiast",
  "avatarPath": "user_icon.png",
  "publicKey": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----",
  "createdAt": "2025-05-29T10:00:00Z"
}
```

---

## 🔄 **Import Workflow**

### "Import My Music" Process

1. **User Selects Source Folder/Files**
   - Open file/folder picker
   - User selects MP3/FLAC/etc. files or folder

2. **Metadata Extraction**
   - Read ID3/metadata tags (TagLib#)
   - Extract: Title, Artist, Album, Duration, Cover art

3. **Organize by Artist/Album**
   - Determine target folder:
	 - If album tag exists: `{BaseFolder}/My Music/{Artist}/{Album}/`
	 - If no album: `{BaseFolder}/My Music/{Artist}/_singles_/`
   - Create folder structure if needed

4. **Copy Files**
   - Copy audio file to target folder
   - Preserve original file if possible (or move if user confirms)

5. **Generate Cache Data**
   - Extract cover art → `.cache/{track}_cover.jpg`
   - Generate waveform → `.cache/{track}_waveform.dat`
   - Save metadata → `.cache/{track}_meta.json`
   - Save album metadata → `.cache/album_meta.json`

6. **Update Library Index**
   - Add to in-memory library
   - Save to `library_cache.db` for fast startup

---

## 🚀 **Fast Startup Strategy**

### Problem
Scanning thousands of music files for metadata on every app start is slow.

### Solution: Metadata Cache

1. **First Import**: Extract and cache metadata
2. **Subsequent Startups**:
   - Load library index from `library_cache.db` (instant)
   - Load metadata from `.cache/*_meta.json` files (fast)
   - Only re-scan if:
	 - File modification date changed
	 - File hash changed
	 - Cache missing

3. **Background Validation**:
   - After startup, background thread validates cache
   - Detects new/changed/deleted files
   - Updates cache incrementally

---

## 📊 **Play Count Tracking**

### Registration Rules
- Play count increments when playback reaches X% of track duration (default: 50%)
- Store in `track_meta.json` → `playCount` field
- Track `lastPlayed` timestamp

### Synchronization
- Play counts sync with P2P peers
- Conflict resolution: sum play counts from all peers
- Each user maintains their own play count

---

## 👤 **Profile Icon Generation**

### Process
1. User selects avatar image in Settings
2. Image is cropped to square aspect ratio
3. Resized to 48x48 pixels
4. Rounded corners applied (8px radius)
5. Saved as `%APPDATA%\MeshWave\user_icon.png`

### Usage
- Display next to user's comments
- Display in artist list for local user
- Include hash in comment data for P2P verification

---

## 🔐 **P2P Synchronization Considerations**

### Stable Comment Format
Comments must have stable format for P2P sync:
- Each comment has unique `commentId` (UUID)
- Signed by author's private key
- Includes timestamp for ordering
- Author identified by `authorUserId` (public key hash)

### Conflict Resolution
- Comments are append-only (no edits)
- Deletions marked with tombstone entry
- Latest signature wins for same `commentId`

### File Transfer
- Files identified by SHA-256 content hash
- Waveform and metadata transferred with audio file
- Covers extracted and shared separately

---

## 🛠️ **Implementation Notes**

### Phase 1: Core Infrastructure (Current Sprint)
- [x] Dark theme UI
- [x] Basic playback
- [ ] Settings page with base folder selection
- [ ] Profile editor

### Phase 2: Storage Architecture
- [ ] Implement folder structure
- [ ] Import workflow with copy to base folder
- [ ] Metadata cache generation
- [ ] Artist/Album organization

### Phase 3: Performance & Caching
- [ ] Fast startup with cached metadata
- [ ] Background cache validation
- [ ] Waveform generation and caching
- [ ] Library database (LiteDB)

### Phase 4: User Features
- [ ] Profile icon generation
- [ ] Play count tracking
- [ ] Comment system with user icons
- [ ] Artist statistics

### Phase 5: P2P Integration
- [ ] Comment synchronization
- [ ] Play count synchronization
- [ ] File transfer with metadata

---

**Last Updated**: 2025-05-29  
**Status**: Design phase - implementation pending
