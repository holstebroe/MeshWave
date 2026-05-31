# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (35/35)
- Integration.Tests: passing (12/12)
- **Total: 83 tests passing**

## Current Focus

Milestone D remainder -- Community Sync (in progress), with priority ordered as:
1. Browse + shared catalogue architecture and implementation
2. Library/My Music search implementation
3. Download lifecycle UX (pending/progress/not-downloaded states)
4. Relay fallback hardening

## Recently Completed

- Bootstrap split: `MeshWave.Bootstrap` host + `MeshWave.Bootstrap.Core` coordinator library
- NAT traversal upgraded with rendezvous-coordinated probe window
- Settings network diagnostics expanded with connection counters and attempt details
- Settings save/apply behavior fixed (network setting changes now apply without restart)
- ARM Linux bootstrap baseline added:
  - RID support (`linux-arm`, `linux-arm64`)
  - publish helper script (`scripts/publish-bootstrap-arm.ps1`)

## Open Work (next execution items)

- Shared catalogue strategy decision (replicated vs distributed vs hybrid)
- Browse protocol/storage/UI implementation based on that decision
- Replace "coming soon" search in Library and My Music
- Pending downloads/progress visibility in Browse + Library
- Remove-from-library while preserving list membership as "Not Downloaded"
- Optional relay fallback after direct+rendezvous failure

## Notes

- Backlog contains actionable task-level items.
- Roadmap contains milestone-level sequencing.
- Documentation remains under `Documentation/`; planning under `ProjectPlan/`.
