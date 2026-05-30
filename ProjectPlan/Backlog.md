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
- [ ] Community library ingestion flow (Other Music) driven by peer manifests
- [ ] Comment sync via manifest operations (signed, author-owned; ReplyToId threading)
- [ ] Comment moderation sync (owner soft-delete ops)
- [ ] Social graph sync (friends, groups, follows as signed manifest ops)
- [ ] Comment permission enforcement across peers
- [ ] Likes sync via manifest operations (one like per user per track, signed)
- [ ] User profile sync (display name, avatar hash, IsArtist flag as signed Profile op)
- [ ] Content exchange: TCP file transfer by content hash

## Milestone E: Trust and Aggregate Integrity
- [ ] Sybil-resistance research spike (proof-of-work UserId or web-of-trust score)
- [ ] Audit log / replay verification for play count manifest operations
- [ ] Per-user contribution cap UI (show X plays from Y unique listeners)

## Milestone F: Artist and Fan Profiles  <-- NEXT
- [ ] User role flag -- IsArtist: bool added to UserProfile and User model
- [ ] Extended artist profile fields -- Bio (plain text max 1000 chars), BannerImagePath (local path), BannerImageHash (P2P content hash), Website (URL)
- [ ] Settings tabbed layout -- replace linear scroll with tabs: General | Profile | Artist | Appearance | Network | Storage
- [ ] Profile tab -- display name, avatar picker, avatar preview (existing content moved here)
- [ ] Artist tab -- conditionally enabled when IsArtist=true; fields: Bio, Website, Banner image picker and preview
- [ ] Artist profile card view -- read-only card shown in Community when browsing a peer; displays banner, rounded avatar, display name, bio, website, track/album count, Follow button
- [ ] Release timestamp -- ReleasedAt: DateTime field on track/album sidecar; set on first announce; shown in library and community views
- [ ] Release feed panel in CommunityView -- lists recent Create manifest ops from followed peers ordered by ReleasedAt; shows artist, title, timestamp, Add to Library button
- [ ] Add to Library flow -- triggers content exchange request; places files in Other Music folder
- [ ] Follow notifications -- badge on Community nav item when followed artist has new Create ops since last sync
- [ ] Persist follow list as signed Follow manifest ops (social graph)
- [ ] User profile sync op -- broadcast IsArtist, Bio, BannerImageHash, Website as signed Profile manifest op

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

## Milestone H: Settings Storage and Housekeeping Tab
- [ ] Storage tab added to Settings (alongside General/Profile/Artist/Appearance/Network)
- [ ] Show used/free disk space and per-category breakdown: My Music, Other Music, Manifests, Cache
- [ ] Visual progress bar per category (green < 70%, amber < 90%, red >= 90%)
- [ ] Clear cached peer manifests button -- deletes PeerManifests/ folder contents and reloads store
- [ ] Clear waveform cache button (future use)
- [ ] Configurable storage quota warning threshold (default 10 GB)

---

## Architecture Notes

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
