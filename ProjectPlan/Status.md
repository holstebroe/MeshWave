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
2. Play count registration — local increment persisted in `.mymusic.json` sidecar; P2P broadcast and consensus designed (see Backlog Architecture Notes)
3. Social graph model — Friends / Groups / Follows; comment permission policy per album/track
4. Library/community data model progression (stats and persistence)
5. My Music release/version lifecycle UX (draft vs released, update track preserving metadata)

## Architecture Decisions

- **User-owned data principle:** all user-generated content (tracks, play counts, comments, likes, profile, chat) is propagated as *signed manifest operations*; no peer can forge another user's data.
- **Play count consensus:** aggregate = sum of per-user counts; each user's contribution is rate-capped by `SecurityLimits.MaxPlaysPerUserPerTrackPerDay` enforced during `MergeManifest`. See Backlog Architecture Notes for full design.

## For later
- Sybil-resistance / web-of-trust hardening for play count integrity
- Networking and P2P sync beyond manifest exchange (content/file transfer)
