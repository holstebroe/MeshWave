# MeshWave Status Snapshot

## Build/Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)

## Documentation Structure

This repository now uses:

- `Documentation/` for architecture and user-facing docs
- `ProjectPlan/` for implementation plans, backlog, and development status

## Active Focus

1. Profile persistence and icon generation
2. ? Play count sync — signed \Play\ ops in local manifest; session rate cap (one per track); \RecordPlay\ wired via \IsPlaying\ property change in ApplicationViewModel
3. ? Play count consensus — \MergeManifest\ enforces \MaxPlaysPerUserPerTrackPerDay=3\ per (trackId, utcDate) per user
4. Social graph model — Friends / Groups / Follows; comment permission policy (next focus)
5. Community mesh menu implemented (scaffold); next: wire real PeerManifestStore data into search/display
6. Per-peer manifest store complete
7. Bootstrap console node (MeshWave.Bootstrap) created

## Architecture Decisions

- **User-owned data principle:** all user-generated content (tracks, play counts, comments, likes, profile, chat) is propagated as *signed manifest operations*; no peer can forge another user's data.
- **Play count consensus:** aggregate = sum of per-user counts; each user's contribution is rate-capped by `SecurityLimits.MaxPlaysPerUserPerTrackPerDay` enforced during `MergeManifest`. See Backlog Architecture Notes for full design.

## For later
- Sybil-resistance / web-of-trust hardening for play count integrity
- Networking and P2P sync beyond manifest exchange (content/file transfer)


