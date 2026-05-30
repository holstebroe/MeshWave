# MeshWave Backlog

## Milestone A: Core Playback (done)
- [x] Basic audio playback (NAudio)
- [x] Waveform styles: Filled, Cloudy, Mirror, Neon, Smooth
- [x] Timeline comments and markers
- [x] Track versioning
- [x] Waveform style selector in Settings

## Milestone B: Library Management (done)
- [x] File scanner (My Music / Other Music folders)
- [x] Album/track list views with .mymusic.json sidecar metadata
- [x] Library ViewModel + View

## Milestone C: Library and Persistence
- [ ] File-based DB or lightweight index for faster startup
- [ ] More robust artist/album statistics
- [ ] Improved search/filter and sorting

## Milestone D: Community Sync
- [x] P2P foundation: PeerDiscovery, ManifestExchangeServer/Client, SyncOrchestrator
- [x] Manifest signing + verification using RSA
- [x] SecurityLimits -- central constants enforced at TCP layer and manifest merge
- [x] P2PIdentityService -- persistent RSA keypair, UserId derived from public key fingerprint
- [x] PeerRouter -- unified routing table: LAN UDP + bootstrap nodes + PEX maintenance loop
- [x] PEX wire protocol, capped at SecurityLimits.MaxPeersPerExchange
- [x] AppSettings.P2PSettings.BootstrapNodes -- configurable internet bootstrap nodes
- [x] Wire SyncOrchestrator into ApplicationViewModel
- [x] Per-peer manifest disk persistence (PeerManifestStore)
- [x] Play count sync -- signed Play ops, session rate cap, RecordPlay on IsPlaying
- [x] Play count consensus -- MergeManifest enforces MaxPlaysPerUserPerTrackPerDay=3
- [x] Community library ingestion flow (Other Music) driven by peer manifests
- [x] Comment sync via manifest operations (signed, author-owned; ReplyToId threading)
- [ ] Comment moderation sync (owner soft-delete ops)
- [x] Social graph sync (friends, groups, follows as signed manifest ops)
- [ ] Comment permission enforcement across peers
- [x] Likes sync via manifest operations (one like per user per track, signed)
- [x] User profile sync (display name, avatar hash, IsArtist flag as signed Profile op)
- [x] Content exchange: TCP file transfer by content hash (NAT hole-punch prep via UDP probes before direct transfer attempts)
- [x] Bootstrap rendezvous ("crossing hands") phase 1: explicit rendezvous session ID issuance by bootstrap coordinator
- [x] Bootstrap rendezvous phase 2: coordinated simultaneous outbound probe window (TCP SYN + UDP punch hints)
- [ ] Relay fallback (opt-in): bootstrap-assisted relay only when direct methods fail
- [x] Connection diagnostics panel: show per-attempt outcomes, local/remote endpoints, and recommended NAT forwarding rules

## Milestone E: Trust and Aggregate Integrity
- [ ] Sybil-resistance research spike (proof-of-work UserId or web-of-trust score)
- [ ] Audit log / replay verification for play count manifest operations
- [ ] Per-user contribution cap UI (show X plays from Y unique listeners)

## Milestone F: Artist and Fan Profiles  (DONE)
- [x] User role flag -- IsArtist: bool added to UserProfile and User model
- [x] Extended artist profile fields -- Bio (plain text max 1000 chars), BannerImagePath (local path), BannerImageHash (P2P content hash), Website (URL)
- [x] Settings tabbed layout -- replace linear scroll with tabs: General | Profile | Artist | Appearance | Network | Storage
- [x] Profile tab -- display name, avatar picker, avatar preview (existing content moved here)
- [x] Artist tab -- conditionally enabled when IsArtist=true; fields: Bio, Website, Banner image picker and preview
- [x] Release timestamp -- ReleasedAt: DateTime? field on Track and Album models; stamped by AnnounceTrack/AnnounceAlbum into manifest metadata
- [x] Release feed panel in CommunityView -- Feed tab: lists ReleaseFeedItem entries ordered newest-first; Refresh button; empty state; "Add to Library" button (action stub)
- [x] Artist profile card view -- Following tab upgraded to full artist cards: banner strip, rounded avatar, ARTIST badge, bio, website, track/follower counts, Unfollow button; Discover cards also show ARTIST badge + bio snippet
- [x] Add to Library flow -- triggers content exchange request; places files in Other Music folder
- [x] Follow notifications -- badge on Community nav item when followed artist has new Create ops since last sync
- [x] Persist follow list as signed Follow manifest ops (social graph)
- [x] User profile sync op -- broadcast IsArtist, Bio, BannerImageHash, Website as signed Profile manifest op

## Milestone G: Community Groups and Distributed Chat

### Design Philosophy

Groups are first-class distributed entities with no central server. Each group is identified
by a group manifest -- an append-only signed log of group events (join, leave, post, channel
create, moderation). Any peer can host and exchange group manifests exactly like personal
manifests. The GroupId is derived from the hash of the founding operation combined with the
founder UserId, making it globally unique without any central registration.

Example groups: Roland Synth Junkies, Berlin Techno Producers, Ambient Drone Collective.

### Group Model
- GroupManifest -- parallel to user Manifest; fields: GroupId, Name, Description, Tags,
  FounderUserId, IsPublic, Channels, BannedUserIds
- Group discovery: peers broadcast known GroupId list in PEX metadata; interested peers
  fetch the full group manifest on demand
- GroupOperationType enum: FoundGroup | JoinGroup | LeaveGroup | PostMessage |
  CreateChannel | DeleteMessage | PromoteModerator | BanUser

### Channels and Posts
- A Channel has ChannelId, Name, Topic, CreatedBy, CreatedAt
- A PostMessage op carries: ChannelId, Text (<=2000 chars), optional AttachmentHash,
  ReplyToOpId for threaded replies
- Posts are ordered by SequenceNumber; clients render history by replaying ops in order
- Attachments are content-addressed files fetched via the content exchange layer

### Moderation and Trust
- Founders and promoted moderators may append DeleteMessage (soft tombstone) and BanUser ops
- Banned users ops are hidden client-side but kept in the manifest (append-only integrity)
- Rate limits in SecurityLimits: MaxGroupPostsPerUserPerDay, MaxGroupsPerUser,
  MaxGroupNameLength, MaxChannelNameLength

### Group Sync Infrastructure
- Group manifests exchanged via the same ManifestExchangeServer/Client TCP infrastructure
- New GroupManifestStore mirrors PeerManifestStore -- persists group manifests by GroupId
- SyncOrchestrator extended: FoundGroup, JoinGroup, LeaveGroup, PostToChannel,
  GetGroupManifest, GetGroupPosts

### Implementation tasks
- [ ] GroupManifest + GroupOperation + Channel models in MeshWave.Common.Core
- [ ] GroupOperationType enum
- [ ] SecurityLimits additions: MaxGroupPostsPerUserPerDay, MaxGroupsPerUser, MaxGroupNameLength, MaxChannelNameLength
- [ ] GroupManifestStore -- disk persistence (mirrors PeerManifestStore)
- [ ] GroupManager -- signing, verification, merge for group manifests (mirrors ManifestManager)
- [ ] Wire group sync into SyncOrchestrator
- [ ] CommunityViewModel expanded -- discovered groups, joined groups, channel list, post list
- [ ] CommunityView -- Groups tab: joined/discovered list; channel sidebar; post thread; reply box
- [ ] Group discovery panel -- search by name/tag; Join/Leave actions
- [ ] Group creation flow -- name, description, tags, initial channel; broadcasts FoundGroup + CreateChannel ops

## Milestone H: Settings Storage and Housekeeping Tab (DONE)
- [x] Storage tab added to Settings (alongside General/Profile/Artist/Appearance/Network)
- [x] Show used/free disk space and per-category breakdown: My Music, Other Music, Manifests, Cache
- [x] Visual progress bar per category (green < 70%, amber < 90%, red >= 90%)
- [x] Clear cached peer manifests button -- deletes PeerManifests/ folder contents and reloads store
- [x] Clear waveform cache button (future use)
- [x] Configurable storage quota warning threshold (default 10 GB)

## Milestone I: Mesh Resilience and Background Mode (COMPLETE)

### Bootstrap Re-contact
- [x] MaintenanceLoopAsync in PeerRouter does periodic PEX (every 2 min)
- [x] PeerRouter: periodic bootstrap re-contact inside MaintenanceLoopAsync (every 5 min)
      so that if a bootstrap node restarts new users can still join without app restart
- [x] SecurityLimits.BootstrapRetryIntervalMinutes constant (default 5)

### System Tray and Background Mode
- [x] App.xaml: ShutdownMode = OnExplicitShutdown
- [x] App.xaml.cs: intercept window close event -- hide window instead of exit
- [x] Add NotifyIcon (System.Windows.Forms) in App.xaml.cs with MeshWave icon
- [x] Tray context menu: "Open MeshWave", "Now Playing", "Quit"
- [x] Windows balloon/toast notification on first minimize-to-tray to inform user
- [x] Add UseWindowsForms to MeshWave.csproj (required for NotifyIcon)
- [x] Quit action: stop P2P cleanly then call Application.Current.Shutdown()
- [x] MainWindow: override OnClosing to redirect to hide when tray is active

## Milestone J: Mesh Integration Tests (DONE)

## Milestone D (current focus): Community Sync remainder

### Goals
- Spin up a real bootstrap node in-process, connect multiple SyncOrchestrator instances,
  verify peer discovery, manifest exchange, and play count sync across peers.
- Extend NAT traversal to include rendezvous-coordinated "crossing hands" before any relay fallback.

### Tests
- [x] New project: MeshWave.Integration.Tests (xUnit, references Synchronizer + Common.Core)
- [x] NullPeerDiscovery stub to suppress UDP broadcast in tests
- [x] Bootstrap_LateJoiner_CanDiscoverExistingPeer
      -- verify late-joining peer can bootstrap via an existing node
- [x] Bootstrap_PeriodicRetry_IntervalIsConfigured
      -- verify bootstrap retry interval is configured in SecurityLimits
- [x] Bootstrap_CanRunOn39877_WhileClientListensOnDifferentConfiguredPort
      -- verify canonical bootstrap port and custom peer listen ports coexist
- [x] BootstrapCoordinator_RegistersConnectedClients_AndSharesViaPex
      -- verify extracted bootstrap coordinator library registers peers and serves PEX
- [x] RequestContentAsync_RecordsAttempts_AndProducesNatGuidance_WhenTransferFails
      -- verify ordered connection attempts and concrete NAT guidance fallback
- [x] ManifestExchange_SignedOperation_IsVerifiable
      -- verify track announcements are signed and verifiable
- [x] ManifestExchange_ProfileBroadcast_IsRecorded
      -- verify profile operations are recorded in manifest
- [x] ManifestExchange_FollowUnfollow_AreRecorded
      -- verify follow/unfollow operations are recorded
- [x] ManifestMerged_Event_FiresCorrectly
      -- verify ManifestMerged event mechanism is wired
- [x] ManifestExchange_TamperedOperation_FailsSignatureCheck
      -- verify tampering is detectable by signature mismatch
- [x] All 12 integration tests passing

---

### User-owned data principle
All user-generated content (tracks, play counts, comments, likes, profile, posts) is
propagated as signed manifest operations. No peer can forge another users data.

### Play count consensus
Aggregate = sum of per-user counts; each user capped at MaxPlaysPerUserPerTrackPerDay
enforced during MergeManifest.

### Artist role
IsArtist is stored in UserProfile locally and broadcast in a signed Profile manifest op.
Fans and artists are equal P2P peers -- the role controls which UI surfaces are shown
and which manifest ops are authored (only artists announce tracks and albums).

### Community group identity
A group has no central owner after founding. The GroupId hash makes it globally unique.
Groups survive founder departure as long as any member holds a copy of the manifest.
Moderation is cooperative -- bans and deletions are visible but enforcement is client-side.
