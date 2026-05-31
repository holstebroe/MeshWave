# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (35/35)
- Integration.Tests: passing (16/16)
- **Total: 87 tests passing**

## Current Focus

Milestone D remainder -- Community Sync (in progress), with priority ordered as:
1. Browse + shared catalogue architecture and implementation
2. Library/My Music search implementation
3. Download lifecycle UX (pending/progress/not-downloaded states)
4. Relay fallback hardening
5. NAT: outbound-only peer manifest push (peer without open port should push to listener)

## Recently Completed

- **Content download protocol wired end-to-end**: `ManifestExchangeClient.RequestContentAsync`,
  `SyncOrchestrator.StartAsync` content provider parameter, `ManifestExchangeServer` `RequestContent`
  type + `ContentBytes` response field
- **Two new integration tests**: John/Jane artist discovery; Jane downloads John's DeskPlastic
  track by content hash from real TestData MP3 files
- **Local manifest persistence**: Follow/Friend/Profile ops now saved to disk and reloaded on
  startup — Friends and Following lists survive application restarts
- **Self excluded from Discover**: local user no longer appears in the peer discovery list
- **Follower/track counts fixed**: counts now include local manifest ops; accurate on both sides
- **Online/offline indicator**: Friends and Following lists show live Online/Offline status
  from the routing table; updates automatically on peer connect/disconnect
- **Discover rebuilt from manifest on startup**: Friends and Following collections are
  repopulated from persisted local manifest operations when CommunityViewModel is created
- **Build error fixed**: missing `{` in `RebuildLikesIndex` after partial edit
- **Command-line instance overrides added**: MeshWave now supports launch-time overrides for
  settings root/appdata root, display name, base folder, P2P enabled/listener mode, port,
  bootstrap nodes, max peers, upload/download limits (enables multi-instance local peer simulation)
- **Persistence root refactor**: settings/profile/identity/local-manifest/peer-manifest and storage
  diagnostics now use a shared overridable appdata root (`MeshWaveEnvironment`) instead of hardcoded
  `%AppData%\MeshWave`
- **Peer discovery/merge reliability fix**: manifest push now carries explicit announcing peer metadata;
  bootstrap and sync merge paths now preserve peer public key + listener port, fixing mesh discovery
  and preventing "0 tracks discovered" due to unverifiable/misrouted peers
- **Manifest fanout hardening**: profile/follow/friend/album/track ops now persist immediately and
  best-effort push to discovered peers
- **New integration test**: `AnnouncedTracks_ArePushedToPeers_WithoutManualManifestPush` reproduces
  bootstrap-assisted discovery + manifest propagation path and now passes
- **Automatic release publish on connect/load**: My Music now auto-announces album/track items marked
  released (`IsReleased=true`) when P2P is connected, reducing the chance of artist peers showing 0 tracks
- **Detailed diagnostics window**: Settings → Network now includes "Open Detailed Diagnostics" showing
  local published counts, per-peer published album/track counts, online status/endpoints, and recent
  per-peer exchange message logs (push/fetch/content success/failure)
- **Quick app controls from branding area**: right-click on the top-left MeshWave logo now exposes
  "Minimize to Tray" and "Quit" actions for faster restart testing
- **Diagnostics consistency + usability pass**: detailed diagnostics now clearly separates routing peers
  (mesh vs bootstrap), shows manifest availability per peer, keeps peer endpoint (`ip:port`) visible,
  and presents the summary in a read-only copyable textbox
- **Sidebar mesh status readability**: connected status text now wraps instead of truncating mid-label

## Open Work (next execution items)

- Shared catalogue strategy decision (replicated vs distributed vs hybrid)
- Browse protocol/storage/UI implementation based on that decision
- Replace "coming soon" search in Library and My Music
- Pending downloads/progress visibility in Browse + Library
- Remove-from-library while preserving list membership as "Not Downloaded"
- Optional relay fallback after direct+rendezvous failure
- NAT: outbound-only peer should push its manifest via bootstrap when no direct listener port

## Notes

- Backlog contains actionable task-level items.
- Roadmap contains milestone-level sequencing.
- Documentation remains under `Documentation/`; planning under `ProjectPlan/`.
