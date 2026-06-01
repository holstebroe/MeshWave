# MeshWave Backlog

## Priority Now (clear execution order)

### [P0 -- Community Browse + Shared Catalogue (highest)](https://github.com/holstebroe/MeshWave/milestone/1)
- [ ] [Write architecture decision: replicated metadata index vs distributed search vs hybrid model](https://github.com/holstebroe/MeshWave/issues/17)
- [ ] [Define shared catalogue schema for Artist/Album/Track/Playlist metadata and peer availability](https://github.com/holstebroe/MeshWave/issues/14)
- [ ] [Implement catalogue sync/index pipeline (ingest, dedupe, staleness rules)](https://github.com/holstebroe/MeshWave/issues/15)
- [ ] [Build Browse UI for artists/albums/tracks/playlists with download actions](https://github.com/holstebroe/MeshWave/issues/13)
- [ ] [Implement play-while-downloading flow (buffered start)](https://github.com/holstebroe/MeshWave/issues/16)
- [ ] [Add global pending downloads queue UI with per-item progress/state](https://github.com/holstebroe/MeshWave/issues/12)

### [P1 -- Library/Local Music search (replace "coming soon")](https://github.com/holstebroe/MeshWave/milestone/2)
- [ ] [Define local search behavior (fields, tokenization, matching, ranking, empty-state UX)](https://github.com/holstebroe/MeshWave/issues/18)
- [ ] [Implement Local Music search for tracks/albums/artists](https://github.com/holstebroe/MeshWave/issues/20)
- [ ] [Implement Library search for tracks/albums/artists/playlists](https://github.com/holstebroe/MeshWave/issues/19)

### [P1 -- Library download lifecycle UX](https://github.com/holstebroe/MeshWave/milestone/3)
- [x] Show pending downloads in Library views with progress indicators
- [x] Support remove-from-library while keeping list membership as "Not Downloaded" state
- [x] Define and apply consistent wording/state for removed-but-discoverable items
- [x] Create artist/album folder placeholder on download enqueue (before bytes arrive)
- [x] Fix pending download tracks appearing under wrong artist in Library (cross-artist album name collision)

### P2 -- Artist/Album folder rename tracking
- [ ] [Design: write a small `.meshwave-id` JSON sidecar file into each artist and album folder on creation, containing a stable GUID and the original entity ID (UserId for artist, AlbumId for album)](https://github.com/holstebroe/MeshWave/issues/21)
- [ ] [On library scan, read sidecar files to correlate folders to their peer entity even after rename](https://github.com/holstebroe/MeshWave/issues/23)
- [ ] [When a peer profile or album name changes (manifest Profile/Update op), locate the local folder via sidecar GUID and rename it to the new readable name](https://github.com/holstebroe/MeshWave/issues/24)
- [ ] [Handle edge cases: missing sidecar (folder created before feature), manual renames, cross-device sync](https://github.com/holstebroe/MeshWave/issues/22)

## Milestone A: Core Playback (done)
- [x] Basic audio playback (NAudio)
- [x] Waveform styles: Filled, Cloudy, Mirror, Neon, Smooth
- [x] Timeline comments and markers
- [x] Track versioning
- [x] Waveform style selector in Settings

## Milestone B: Library Management (done)
- [x] File scanner (Local Music / Peer Music folders)
- [x] Album/track list views with .mymusic.json sidecar metadata
- [x] Library ViewModel + View

## [Milestone C: Library and Persistence](https://github.com/holstebroe/MeshWave/milestone/4)
- [ ] [File-based DB or lightweight index for faster startup](https://github.com/holstebroe/MeshWave/issues/25)
- [ ] [More robust artist/album statistics](https://github.com/holstebroe/MeshWave/issues/27)
- [ ] [Improved search/filter and sorting](https://github.com/holstebroe/MeshWave/issues/26)

## [Milestone D: Community Sync](https://github.com/holstebroe/MeshWave/milestone/5)
- [x] P2P foundation: PeerDiscovery, ManifestExchangeServer/Client, SyncOrchestrator
- [ ] [Implement delta manifest synchronization (request operations by `SequenceNumber` range)](https://github.com/holstebroe/MeshWave/issues/30)
- [ ] [Implement manifest compaction/snapshotting (signed state checkpoints to squash old operations, especially operations where the exact history is unimportant. Play count, etc.)](https://github.com/holstebroe/MeshWave/issues/31)
- [ ] [Migrate manifest wire format to a compact binary format (e.g., Protobuf or MessagePack)](https://github.com/holstebroe/MeshWave/issues/32)
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
- [x] Community library ingestion flow (Peer Music) driven by peer manifests
- [x] Comment sync via manifest operations (signed, author-owned; ReplyToId threading)
- [ ] [Comment moderation sync (owner soft-delete ops)](https://github.com/holstebroe/MeshWave/issues/28)
- [x] Social graph sync (friends, groups, follows as signed manifest ops)
- [ ] [Comment permission enforcement across peers](https://github.com/holstebroe/MeshWave/issues/29)
- [x] Likes sync via manifest operations (one like per user per track, signed)
- [x] User profile sync (display name, avatar hash, IsArtist flag as signed Profile op)
- [x] Content exchange: TCP file transfer by content hash (NAT hole-punch prep via UDP probes before direct transfer attempts)
- [x] Bootstrap rendezvous ("crossing hands") phase 1: explicit rendezvous session ID issuance by bootstrap coordinator
- [x] Bootstrap rendezvous phase 2: coordinated simultaneous outbound probe window (TCP SYN + UDP punch hints)
- [ ] [Relay fallback (opt-in): bootstrap-assisted relay only when direct methods fail](https://github.com/holstebroe/MeshWave/issues/33)
- [x] ARM Linux bootstrap publish script baseline (`scripts/publish-bootstrap-arm.ps1`)
- [x] Connection diagnostics panel: show per-attempt outcomes, local/remote endpoints, and recommended NAT forwarding rules
- [x] Diagnostics consistency pass: distinguish routing peers vs mesh/bootstrap peers, show manifest availability,
      keep peer endpoint visibility, and make summary text copyable

## [Milestone E: Trust and Aggregate Integrity](https://github.com/holstebroe/MeshWave/milestone/6)
- [ ] [Sybil-resistance research spike (proof-of-work UserId or web-of-trust score)](https://github.com/holstebroe/MeshWave/issues/36)
- [ ] [Audit log / replay verification for play count manifest operations](https://github.com/holstebroe/MeshWave/issues/34)
- [ ] [Per-user contribution cap UI (show X plays from Y unique listeners)](https://github.com/holstebroe/MeshWave/issues/35)

## Milestone F: Artist and Fan Profiles  (DONE)
- [x] User role flag -- IsArtist: bool added to UserProfile and User model
- [x] Extended artist profile fields -- Bio (plain text max 1000 chars), BannerImagePath (local path), BannerImageHash (P2P content hash), Website (URL)
- [x] Settings tabbed layout -- replace linear scroll with tabs: General | Profile | Artist | Appearance | Network | Storage
- [x] Profile tab -- display name, avatar picker, avatar preview (existing content moved here)
- [x] Artist tab -- conditionally enabled when IsArtist=true; fields: Bio, Website, Banner image picker and preview
- [x] Release timestamp -- ReleasedAt: DateTime? field on Track and Album models; stamped by AnnounceTrack/AnnounceAlbum into manifest metadata
- [x] Release feed panel in CommunityView -- Feed tab: lists ReleaseFeedItem entries ordered newest-first; Refresh button; empty state; "Add to Library" button (action stub)
- [x] Artist profile card view -- Following tab upgraded to full artist cards: banner strip, rounded avatar, ARTIST badge, bio, website, track/follower counts, Unfollow button; Discover cards also show ARTIST badge + bio snippet
- [x] Add to Library flow -- triggers content exchange request; places files in Peer Music folder
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
  FounderUserId, IsPublic, CoverImageHash, Channels, BannedUserIds, AdminUserIds
- Group discovery: peers broadcast known GroupId list in PEX metadata; interested peers
  fetch the full group manifest on demand
- GroupOperationType enum: FoundGroup | JoinGroup | LeaveGroup | PostMessage |
  CreateChannel | DeleteMessage | PromoteModerator | DemoteModerator | BanUser | KickUser |
  ApproveInviteRequest | UpdateGroupProfile | InviteRequest

### Group Membership and Access Control
- Any user can create (found) a new group with a name, description, cover image, and initial
  channel; the founder is automatically the first administrator
- A group is either **open** (any peer can join by appending a JoinGroup op) or
  **invite-only** (joining requires an InviteRequest op to be approved by an administrator
  who then appends an ApproveInviteRequest op)
- Administrators are tracked as a list in the group manifest; the founder can promote/demote
  any member; there must always be at least one administrator
- Administrators can kick (remove) existing members and ban peers from re-joining
- Banned user ops are soft tombstones -- stored in the manifest for audit but hidden client-side
- Rate limits in SecurityLimits: MaxGroupPostsPerUserPerDay, MaxGroupsPerUser,
  MaxGroupNameLength, MaxChannelNameLength

### Group Profile Page
- Each group has a discoverable profile page showing: cover image, title, description,
  member count, admin list, tags/genre labels, creation date
- Administrators can edit title, description, cover image, and tags via an UpdateGroupProfile op
- Profile page includes a Join / Request Invite button and a channel list

### Channels and Posts
- A Channel has ChannelId, Name, Topic, CreatedBy, CreatedAt
- A PostMessage op carries: ChannelId, Text (<=2000 chars), optional AttachmentHash,
  ReplyToOpId for threaded replies
- Posts are ordered by SequenceNumber; clients render history by replaying ops in order
- Attachments are content-addressed files fetched via the content exchange layer

### [Moderation and Trust](https://github.com/holstebroe/MeshWave/milestone/7)
- Founders and promoted moderators may append DeleteMessage (soft tombstone) and BanUser/KickUser ops
- Rate limits in SecurityLimits: MaxGroupPostsPerUserPerDay, MaxGroupsPerUser,
  MaxGroupNameLength, MaxChannelNameLength

### Group Sync Infrastructure
- Group manifests exchanged via the same ManifestExchangeServer/Client TCP infrastructure
- New GroupManifestStore mirrors PeerManifestStore -- persists group manifests by GroupId
- SyncOrchestrator extended: FoundGroup, JoinGroup, LeaveGroup, PostToChannel,
  GetGroupManifest, GetGroupPosts, ApproveInvite, KickMember

### Implementation tasks
- [ ] [GroupManifest + GroupOperation + Channel models in MeshWave.Common.Core](https://github.com/holstebroe/MeshWave/issues/45)
- [ ] [GroupOperationType enum (including InviteRequest + ApproveInviteRequest)](https://github.com/holstebroe/MeshWave/issues/47)
- [ ] [SecurityLimits additions: MaxGroupPostsPerUserPerDay, MaxGroupsPerUser,](https://github.com/holstebroe/MeshWave/issues/49)
      MaxGroupNameLength, MaxChannelNameLength, MaxGroupDescriptionLength,
      MaxGroupTagsCount, MaxGroupAdminsCount
- [ ] [GroupManifestStore -- disk persistence (mirrors PeerManifestStore)](https://github.com/holstebroe/MeshWave/issues/46)
- [ ] [GroupManager -- signing, verification, merge for group manifests (mirrors ManifestManager)](https://github.com/holstebroe/MeshWave/issues/44)
- [ ] [Wire group sync into SyncOrchestrator (FoundGroup, Join, Leave, Post, Moderate ops)](https://github.com/holstebroe/MeshWave/issues/50)
- [ ] [CommunityViewModel expanded -- group discovery, joined groups, channel list,](https://github.com/holstebroe/MeshWave/issues/40)
      post list, invite request queue (for admins), membership management
- [ ] [CommunityView -- Groups tab: joined/discovered list; channel sidebar; post thread; reply box](https://github.com/holstebroe/MeshWave/issues/39)
- [ ] [Group discovery panel -- search by name/tag; Join (open) / Request Invite (closed) actions](https://github.com/holstebroe/MeshWave/issues/42)
- [ ] [Group creation flow -- name, description, tags, cover image, privacy setting,](https://github.com/holstebroe/MeshWave/issues/41)
      initial channel; broadcasts FoundGroup + CreateChannel ops
- [ ] [Group profile page -- view/edit title, description, cover image, tags; member/admin list;](https://github.com/holstebroe/MeshWave/issues/43)
      Join/Request Invite CTA
- [ ] [Admin panel -- pending invite requests list with Approve/Deny; member list with](https://github.com/holstebroe/MeshWave/issues/37)
      Kick/Ban/Promote actions; online indicators per member
- [ ] [Admin promote/demote moderator flow with confirmation dialog](https://github.com/holstebroe/MeshWave/issues/38)
- [ ] [Open vs invite-only toggle in group settings (stored as IsPublic in group manifest)](https://github.com/holstebroe/MeshWave/issues/48)

### Future: Competition Feature (Roadmap)
- See Roadmap.md for the full competition feature design (ballot-sealed voting, deadlines,
  playlist lock, admin-decrypted tally)

## Milestone H: Settings Storage and Housekeeping Tab (DONE)
- [x] Storage tab added to Settings (alongside General/Profile/Artist/Appearance/Network)
- [x] Show used/free disk space and per-category breakdown: Local Music, Peer Music, Manifests, Cache
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

