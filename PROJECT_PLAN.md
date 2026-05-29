# MeshWave — Project Plan

## 1. Summary
MeshWave is a server-less, peer-to-peer music-sharing app for hobby/community musicians. Initial deliverable: a Windows WPF frontend + local library manager + a synchronizer that exchanges music, metadata and comments in a distributed mesh. Built with Visual Studio 2026, C# 14, .NET 10.

---

## 2. Goals & non-goals
- Goals
  - P2P music exchange (no central server required for content delivery)
  - Signed ownership model: only owners can modify their content
  - Local library manager + UI for playback, metadata editing and comments (time-linked)
  - Community catalogue discovery and selective download (group / user / album / track)
- Non-goals (MVP)
  - Mobile or web frontends (defer for later)
  - Heavy cryptocurrency-style blockchain; prefer signed append-only manifests
  - Global STUN/TURN infrastructure (optional helpers may be suggested)

---

## 3. High-level architecture
- Frontend (WPF): playback UI, library browser, settings, user profile, comment UI, waveform visualization
- Library Manager: local catalog, indexing, metadata editor, cover image management
- Synchronizer: P2P network layer, discovery, file transfer, manifest exchange, conflict/tombstone handling
- Common core library: domain models (User, Community, Album, Track, Comment), crypto utilities, storage abstraction, sync protocol implementation
- Optional helper services (bootstrap peers or relays) — not required but can improve connectivity

---

## 4. Data model (MVP)
- Identity: UserId = publicKey fingerprint (GUID derived from public key). DisplayName editable.
- Track: { trackId, albumId?, ownerUserId, title, duration, fileHash, fileSize, coverHash?, metaVersion, signature }
- Album: { albumId, ownerUserId, title, coverHash?, trackIds[], signature }
- Comment: { commentId, authorUserId, targetType(album|track), targetId, timestampInTrack?(seconds), text, createdAt, signature }
- Manifest (per-user): append-only list of signed operations (create/update/delete/tombstone) referencing content by content-hash
- File storage: content-addressed (SHA-256) blobs stored locally, referenced by manifests

Formats: JSON for metadata, binary files for audio and images.

---

## 5. Synchronization & ownership model
- Each user publishes a signed manifest (append-only). New operations are appended and signed by the user's private key.
- Files are exchanged by content-hash. Peers request content by hash.
- Delete/update = owner appends a new signed operation (tombstone or new reference). Sync uses last valid owner-signed op.
- Comments are authored/signed by commenter; only comment author or target owner can delete? (MVP: only comment author can delete; owner can hide via moderation flag.)
- Conflict resolution: last-append-wins for a given manifest (monotonic signed sequence numbers or timestamps). Signatures prevent forgery.

---

## 6. Networking & discovery
- Discovery options:
  - Local LAN: mDNS / UDP broadcast for peers
  - Internet: DHT-like bootstrap (optionally use community-run bootstrap nodes or configurable bootstrap peers)
  - Optional relay/forwarding nodes if NAT traversal fails
- Transport:
  - TCP with resumable chunking, optionally QUIC/WebRTC in later phase
  - Resumable transfers, partial downloads (for streaming)
- NAT traversal:
  - Implement a best-effort NAT traversal strategy. Document that some networks may need relay nodes.

---

## 7. Security & privacy
- Each user generates a keypair on first run (RSA/ECDSA; prefer Ed25519).
- All manifests and comments are signed.
- Optional: encrypt transfers between peers (TLS).
- Users control what to share (selective sync filters).
- Provide clear UI for identity, key backup/export and revocation (simple process for MVP).

---

## 8. Frontend (WPF) features (MVP)
- Setup wizard: choose storage folder, set DisplayName, generate keypair
- Library browser: list communities, users, albums, tracks
- Playback page:
  - Audio playback with seek
  - Waveform visualization and playback cursor
  - Comments panel showing time-linked comments; click to seek
- Library manager page: organize local albums/tracks, edit descriptions and covers, drag-and-drop import
- Sync control: choose groups/users/albums/tracks to follow; sync status view
- Notifications for incoming content and sync progress

Recommended libraries:
- Audio playback: NAudio or managed wrappers (NAudio for decoding + WASAPI/DirectSound)
- Waveform generation: precompute waveform downsampled data; render in WPF canvas

---

## 9. Library manager (MVP)
- Index local files and metadata (incremental watchers)
- Validate file hashes and manifest entries
- Manage local artist folder: auto-create special user folder under main storage
- Provide selective download queues and background sync tasks

---

## 10. Testing & QA
- Unit tests: domain models, manifest signing/verification, storage layer
- Integration tests: simulated P2P exchange between multiple local instances
- Network tests: NAT scenarios using test harness
- UI tests: basic UI flows (playback, comment linking)
- Performance: large library indexing, streaming while downloading

---

## 11. Milestones & suggested timeline (example)
All milestones assume a small team or single developer. Estimate in 2-week sprints.
- Sprint 1: Project setup, core libraries, identity & keypair, storage abstraction, basic models
- Sprint 2: Local library manager (indexing, file hashing), WPF skeleton, setup wizard
- Sprint 3: Playback (NAudio), waveform generation, basic UI for playback and comments
- Sprint 4: Simple P2P discovery (LAN), manifest exchange, content request/resume
- Sprint 5: Signed manifests, ownership enforcement, apply manifests to local catalog, selective download
- Sprint 6: UI polish, sync manager UI, background sync, progress indicators
- Sprint 7: Integration tests, network tests, CI setup, packaging installer
- Sprint 8: Beta release, user feedback, minor fixes

---

## 12. Acceptance criteria (MVP)
- Installable WPF app that can:
  - Create identity and storage folder
  - Index local music and play tracks with waveform + comments UI
  - Discover a peer on LAN and exchange manifests and at least one audio file
  - Allow creating, signing and syncing a comment linked to a time position
  - Owner-signed manifests control modifications

---

## 13. Risks & mitigations
- NAT traversal unreliability — mitigate by supporting optional relay peers and clear docs
- Large files and bandwidth — support resuming, partial streaming and prioritization
- Complexity of true blockchain — use signed append-only manifests instead
- Privacy concerns — let users opt-in to public groups and provide export/delete tools

---

## 14. Next actions (immediate)
1. Create solution skeleton: Common.Core, Synchronizer, LibraryManager, MeshWave.Wpf
2. Implement identity/keypair generation and local manifest format
3. Implement storage abstraction and sample local index
4. Prototype audio playback + waveform rendering
5. Prototype LAN peer discovery and content exchange demo

---

## 15. Deliverables
- Repo with solution and projects scoped above
- Documentation: architecture overview, data format spec, sync protocol spec, setup guide
- Automated tests for core sync and crypto
- MVP installer and release notes
