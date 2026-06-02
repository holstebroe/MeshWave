# MeshWave Status Snapshot

## Build / Test

- Build: passing
- Common.Core tests: passing (32/32)
- LibraryManager tests: passing (4/4)
- Synchronizer tests: passing (35/35)
- Integration.Tests: passing (16/16)
- **Total: 87 tests passing**

## Current Focus

Selected tasks for 2026-06-01:
1. **Issue #9**: Fix track selection bug in player.
   - *Success Criteria*: Double-clicking a track in the list correctly plays that track instead of jumping to the end.
2. **Issue #17**: Architecture decision for shared catalogue.
   - *Success Criteria*: A written decision (ADR) comparing replicated vs distributed vs hybrid models.
3. **Issue #31**: Implement manifest compaction/snapshotting.
   - *Success Criteria*: Protocol support for signed state checkpoints to reduce sync bandwidth.

Milestone D remainder -- Community Sync (in progress), with priority ordered as:
1. Browse + shared catalogue architecture and implementation
2. Library/Local Music search implementation
3. Download lifecycle UX (pending/progress/not-downloaded states)
4. Relay fallback hardening
5. NAT: outbound-only peer manifest push (peer without open port should push to listener)

## Recently Completed

- **Architecture Decision Record (ADR 0001) for Shared Catalogue**: Defined a hybrid replication/distributed search strategy for the mesh catalogue.
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
- **Automatic release publish on connect/load**: Local Music now auto-announces album/track items marked
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
- **Peer content serving fixed**: P2P startup now wires a local content provider so `RequestContent` can resolve
  announced hashes to file bytes (fixes direct probe success but content request failure)
- **Download UX consistency and resilience**: Community Feed action label now matches Browse (`Download`),
  feed downloads use the shared queue path, and failed queue items auto-retry after delay
- **Library pending placeholder visibility**: Library (Peer Music) now shows queued/downloading/failed placeholders
  immediately with status badges and disabled playback until download completes
- **Display-name fallback hardening**: Browse and follow/friend lists now prefer profile displayName,
  then routed peer display name, before GUID fallback
- **Library queue crash fix**: resolved null-reference when no album is selected by making queue placeholders
  robust to `SelectedAlbum == null` and preserving queue album context
- **Queue metadata accuracy**: Community Feed queued items now preserve the track's actual album name
  (instead of hardcoded `Community`) via feed operation metadata
- **Browse download state polish**: track buttons now transition from queued to downloaded correctly,
  and queued state only reflects pending/downloading items (not completed ones)
- **Library status visibility improved**: album rows now show pending/downloading/failed download aggregates,
  and track rows show album name + download status badge for placeholders
- **Download icon polish**: Browse download labels now use explicit state icons (`⬇`, `⏳`, `✅`) for
  faster visual scanning of download state
- **Remove lifecycle completed**: Peer Music tracks can now be removed via context menu while remaining
  discoverable as `Not Downloaded` placeholders; state persists via appdata-backed removal markers and
  auto-clears when a track is downloaded again
- **Library organization fix for pending/removed tracks**: pending and removed placeholder tracks now
  materialize artist/album shells first, so items stay organized under Artists/Albums instead of appearing
  only in the transient Tracks pane
- **Library one-click re-download**: `Re-download` action now available on `Not Downloaded` placeholders,
  re-queues by content hash and peer hint directly from Library
- **Diagnostics peers fallback**: diagnostics window now backfills peer rows from routing table when
  snapshot feed unexpectedly returns empty list
- **Content serving hardening**: local content provider now ignores zero-length files for matched hashes to
  reduce false `content-request: fail` outcomes
- **Track selection fix**: Resolved a race condition where double-clicking a track caused a cascade of
  automatic advances to the end of the list; implemented disposal guards in `AudioPlaybackService` and
  instance validation in `PlaybackViewModel`.

## Open Work (next execution items)

- Browse protocol/storage/UI implementation based on ADR 0001
- Replace "coming soon" search in Library and Local Music (Assigned to Jules Fleet)
- Pending downloads/progress visibility in Browse + Library
- Remove-from-library while preserving list membership as "Not Downloaded"
- Optional relay fallback after direct+rendezvous failure
- NAT: outbound-only peer should push its manifest via bootstrap when no direct listener port

## Notes

- Backlog contains actionable task-level items.
- Roadmap contains milestone-level sequencing.
- Documentation remains under `Documentation/`; planning under `ProjectPlan/`.
