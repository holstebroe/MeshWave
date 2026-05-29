# MeshWave — Project Structure & Architecture

## Solution Overview

MeshWave is organized into 4 main projects:

### 1. **MeshWave.Common.Core** (Class Library)
Core domain models, cryptography utilities, and storage abstraction layer.

**Key Components:**
- **Models/**: Domain entities
  - `User.cs`: User identity with keypair
  - `Track.cs`: Individual music track
  - `Album.cs`: Collection of tracks
  - `Comment.cs`: Time-linked comments on tracks/albums
  - `Manifest.cs`: Append-only signed operation log per user
  - `Community.cs`: Community group organization

- **Crypto/**: Cryptographic utilities
  - `CryptoService.cs`: RSA signing/verification, SHA256 hashing, key generation

- **Storage/**: Persistent storage abstraction
  - `StorageService.cs`: Content-addressed blob storage (by SHA256 hash)

- **Serialization/**: JSON utilities
  - `JsonSerializer.cs`: Model serialization/deserialization

### 2. **MeshWave.LibraryManager** (Class Library)
Local music library indexing, metadata management, and music file organization.

**Key Components:**
- `LocalLibraryManager.cs`: Indexes local music files, tracks, and albums

**Planned Features:**
- File watchers for incremental indexing
- Audio metadata extraction (ID3, etc.)
- Album/track organization
- Drag-and-drop import UI integration

### 3. **MeshWave.Synchronizer** (Class Library)
P2P network layer: peer discovery, manifest exchange, and content transfer.

**Key Components:**
- `ManifestManager.cs`: Creates, signs, and verifies append-only manifests
- `PeerDiscovery.cs`: LAN/internet peer discovery (mDNS, bootstrap nodes)
- `ContentExchange.cs`: P2P file transfer by content hash

**Planned Features:**
- mDNS-based LAN discovery
- Bootstrap peer connectivity
- Resumable content transfers
- NAT traversal support

### 4. **MeshWave** (WPF Application)
Windows Presentation Foundation frontend for browsing, playback, and social features.

**Planned Features:**
- Setup wizard (storage folder, username, keypair generation)
- Library browser (communities, users, albums, tracks)
- Playback UI with waveform visualization
- Time-linked comment system
- Music library manager (organize albums/tracks)
- Sync control panel

---

## Data Model Overview

### User
```json
{
  "userId": "guid-from-public-key-hash",
  "displayName": "CommunityMusician",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----...",
  "description": "optional bio",
  "coverImageHash": "sha256-hash",
  "createdAt": "2024-01-01T00:00:00Z"
}
```

### Track
```json
{
  "trackId": "unique-id",
  "albumId": "optional-album-id",
  "ownerUserId": "owner-user-id",
  "title": "Song Title",
  "duration": "00:03:45",
  "fileHash": "sha256-hash",
  "fileSize": 5242880,
  "coverImageHash": "optional-hash",
  "signature": "rsa-signed-metadata"
}
```

### Album
```json
{
  "albumId": "unique-id",
  "ownerUserId": "owner-user-id",
  "title": "Album Name",
  "coverImageHash": "optional-hash",
  "trackIds": ["track-id-1", "track-id-2"],
  "signature": "rsa-signed-metadata"
}
```

### Comment
```json
{
  "commentId": "unique-id",
  "authorUserId": "author-id",
  "targetType": "Track|Album",
  "targetId": "track-or-album-id",
  "timestampInTrackSeconds": 125.5,
  "text": "Cool bridge!",
  "signature": "rsa-signed-comment"
}
```

### Manifest
Append-only signed operation log per user:
```json
{
  "userId": "user-id",
  "operations": [
	{
	  "operationId": "op-1",
	  "operationType": "Create|Update|Delete",
	  "targetId": "track-id",
	  "targetType": "Track",
	  "contentHash": "sha256-hash",
	  "sequenceNumber": 0,
	  "signature": "rsa-signed-operation"
	}
  ],
  "version": 1,
  "lastUpdated": "2024-01-01T00:00:00Z"
}
```

---

## File Storage Architecture

Files are stored using **content-addressed storage** (by SHA256 hash):

```
StorageBasePath/
├── blobs/
│   ├── [SHA256-HASH-1] (audio file)
│   ├── [SHA256-HASH-2] (image file)
│   └── ...
└── metadata/
	├── [TRACK-ID].json
	├── [ALBUM-ID].json
	└── ...
```

This ensures:
- Deduplication: identical files stored once
- Integrity: file hash verified on every read
- P2P sharing: peers can request files by hash alone

---

## Security & Signing

### Key Generation
Users generate RSA 4096-bit keypairs on first run:
- Private key stored securely (local file or OS credential store)
- Public key distributed via manifests
- User ID derived from SHA256 hash of public key

### Signatures
All user-authored content is signed using their private key:
- Tracks/albums signed by owner only
- Comments signed by commenter
- Manifests: each operation signed by user

### Verification
Content is verified before acceptance:
- Signature checked against author's public key
- Owner-only operations verified (no spoofing)
- Manifest sequence numbers verified (no replay)

---

## Synchronization Protocol (MVP)

1. **Discovery**: Local peers discovered via mDNS or bootstrap nodes
2. **Manifest Exchange**: Peers request and validate manifests
3. **Conflict Resolution**: Last-append-wins for each manifest
4. **Content Request**: Peers request files by SHA256 hash
5. **Selective Sync**: Users choose to follow groups, users, or specific albums

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- Visual Studio 2026 (or VS Code + .NET CLI)
- Windows (for WPF frontend)

### Building
```bash
cd E:\Projects\MeshWave
dotnet build MeshWave.sln
```

### Running
```bash
dotnet run --project MeshWave/MeshWave.csproj
```

---

## Project Status

✅ **Completed:**
- Solution structure (4 projects)
- Domain models (User, Track, Album, Comment, Manifest, Community)
- Cryptographic utilities (RSA-based signing/verification)
- Storage abstraction (content-addressed blob storage)
- JSON serialization
- Placeholder implementations for LibraryManager, Synchronizer

🚧 **In Progress:**
- WPF UI framework and setup wizard
- Library indexing and metadata extraction
- Peer discovery protocol
- Content exchange protocol

📋 **Next Steps (Sprint 1-2):**
1. Implement setup wizard UI
2. Implement file indexing and hashing
3. Implement LAN peer discovery (mDNS)
4. Implement basic manifest exchange protocol

---

## References & Standards

- **Cryptography**: RSA-4096 + SHA256 (aligned with TLS standards)
- **JSON**: UTF-8, camelCase naming
- **PEM**: Standard cryptographic key encoding
- **P2P**: TCP-based with resumable transfers (future: QUIC)

---

## License

[To be determined - add license info]
