# P2P Protocol Review

Review of the MeshWave network protocol against its design goals:

- **Serverless mesh.** Peers exchange signed manifests and files directly.
- **Minimal open ports.** Only one peer (or a standalone bootstrap) needs an open port. The bootstrap's only job is establishing direct connections (discovery, hole-punching); it must never relay data.
- **Scales to many small items** (comments, likes) and many peers.

This document lists what was fixed in the first pass (quick fixes), what was fixed in the connectivity pass (C1–C6), and what remains, with a proposed fix for each remaining item so it can be picked up in a separate session.

## Part 1: Fixed in the first pass

| # | Problem | Fix | Tests |
|---|---|---|---|
| F1 | **Files larger than 512 KB were corrupted on download.** The server ignored `ChunkOffset`/`ChunkLength` (the fields were not even in the protobuf schema) and always sent the whole file. The client then copied bytes from offset 0 into every chunk, and each chunk re-downloaded the whole file. | Added `chunk_offset`, `chunk_length` and `total_content_length` to `meshwave.proto` and the serializer. The server slices the requested range (`ManifestExchangeServer.ResolveContentSlice`). The client slices at the right offset when an old peer sends the whole file (`ParallelChunkStream.ExtractChunk`). | `ContentChunkingTests`, `ManifestSerializerTests`, `LargeContent_SpanningManyChunks_IsDownloadedByteExact` |
| F2 | **Downloads were never verified against their hash.** | `SyncOrchestrator.RequestContentAsync` checks the SHA-256 of the received bytes when the content hash is a 64-character hex SHA-256, and discards mismatches. | `ContentChunkingTests.ContentMatchesHash_*` |
| F3 | **Duplicate operations after merge.** The last applied sequence number was computed from the operation *count*. When an op was discarded (daily play cap, invalid competition op), the next sync re-appended an op that was already stored. Merges could also append ops after a gap. | `ManifestManager.GetHeadSequenceNumber` (highest sequence covered) is used for merge, delta-fetch start, and group-event detection. Merges stop at a gap; discarded ops still advance the expected sequence. | `ManifestMergeSequenceTests` |
| F4 | **Group chat was lost on compaction.** `PostMessage`, `CreateChannel`, `FoundGroup` and `ModerateGroup` were not carried into the snapshot. | Added to the snapshot's persistent operations. | `ManifestMergeSequenceTests.CreateSnapshot_KeepsGroupOperations` |
| F5 | **Identity spoofing.** The key used to verify a manifest was taken from the push, PEX entry or profile op without checking that it belongs to the `UserId`. Anyone could publish as anyone. | `CryptoService.IsPublicKeyForUser`. Enforced for manifest pushes, fetches, announcements, PEX/routing-table entries and bootstrap registrations. | `CryptoServiceTests`, `ForgedManifestPush_WithKeyNotMatchingUserId_IsRejected` |
| F6 | **Listening peers never registered with the bootstrap.** A bootstrap only learned a peer when it received a manifest push, and normal peers never push to the bootstrap. | New `Announce` request. Peers announce on startup and on every bootstrap re-contact (heartbeat). The receiver records the observed source IP. | `Bootstrap_LateJoiner_CanDiscoverExistingPeer`, `Bootstrap_ListeningPeers_ConvergeWithoutForcedPushes` |
| F7 | **"A and B, only A has an open port" did not work.** B saw A only as an anonymous `bootstrap:host:port` entry without a key, so it never fetched A's manifests. Outbound-only peers were registered with a made-up port 39877. There was no periodic sync, so a peer that can't receive pushes never saw later updates. | A regular peer answers `Announce` with its own peer info, so B learns A as a real peer. Outbound-only peers are registered with port 0, and `PeerRouter.IsDialable` keeps everyone from dialling them. `SyncOrchestrator` pulls deltas from all peers every 60 s (`SecurityLimits.PeriodicSyncIntervalSeconds`). | `OnlyOnePeerListening_OutboundOnlyPeerExchangesManifestsBothWays` |
| F8 | **The integration tests could not detect broken discovery.** `Bootstrap_LateJoiner_*` asserted `ConnectedPeerCount >= 0` (always true), and `MeshTestContext.ConnectAndSyncAllAsync` force-pushed every manifest between peers over 127.0.0.1. | Real assertions. `ConnectAndSyncAllAsync` now only waits (and triggers the periodic sync) and throws `TimeoutException` if the mesh does not converge by itself. | All integration tests |

## Part 2: Connectivity, fixed in the second pass

The connection model is now: every peer keeps a **persistent session** with each bootstrap node and with a few neighbours. A session is a TCP connection where the remote has an open port, or a **hole-punched UDP** connection (LiteNetLib, reliable ordered) where neither side has one. Requests and pushes flow in both directions over it. The bootstrap only introduces peers; it never relays data.

| # | Problem | Fix | Tests |
|---|---|---|---|
| C1 | **No persistent, bidirectional connections.** Every exchange opened a new TCP connection to the target's listening port, so peers behind NAT could only poll (every 60 s). | `PeerSession` multiplexes requests in both directions over one connection (frames carry a kind and a request ID; several requests can be in flight). A one-shot `OpenSession` request upgrades an incoming TCP connection (`ManifestExchangeServer.SessionUpgradeHandler`); `PeerSessionManager` owns the sessions. Keepalive every 25 s (`SessionKeepaliveSeconds`), closed after 75 s of silence. Each side sends a `Hello` with a fresh nonce and the answer signs it, so a session is only attributed to a UserId after a challenge-response. Duplicate sessions (both sides dialled) are pruned deterministically (both sides rank by the two nonces). `ManifestExchangeClient` routes every request over a session when one exists (`SessionResolver`), else one-shot TCP. Peers open control sessions to all bootstrap nodes and up to 8 dialable neighbours (`MaxOutboundNeighbourSessions`). Fan-out pushes go to every peer with a session, including outbound-only ones. The periodic pull is now a 5-minute anti-entropy fallback (`PeriodicSyncIntervalSeconds = 300`). | `PeerSessionTests.TcpSession_*`, `TcpSessions_DialledFromBothSides_*`, `OnlyOnePeerListening_OutboundOnlyPeerExchangesManifestsBothWays` (later updates now arrive by push, without polling) |
| C2 | **Hole punching did nothing useful.** The bootstrap had no UDP socket, rendezvous sessions never reached the target, punches went to the TCP port, and all real traffic was TCP. | `UdpSessionHost`: one UDP socket per node (LiteNetLib with `NatPunchModule`, bound to the manifest port number). Any peer with an open port, and the standalone bootstrap, is an **introducer**. B sends `RequestIntroduction(C)` over its session with the introducer A; A issues a single-use token, forwards an `IntroductionOffer` over its session with C, and both B and C send NAT introduce requests with that token to A's UDP port. A observes both public endpoints (like STUN) and sends each side the other's public and private endpoint; both punch at once, connect (only once per token, only accepting connections that carry an expected token), and run the same `PeerSession` protocol (including the `Hello` challenge) over the reliable UDP channel. Peers try this automatically for every non-dialable peer they learn about (`EnsureSessionsAsync`, cooldown 60 s per target) and on demand before a content download (`PrepareConnectionAsync` reports `persistent-session`, `direct-tcp-probe`, `udp-introduction`). The old rendezvous (`RequestRendezvous`, `BootstrapCoordinator.OnRendezvousRequested`, `NatTraversalService.TryPunchAsync`) is removed. | `PeerSessionTests.Introduction_*`, `TwoPeersWithoutOpenPorts_ExchangeManifestsDirectlyOverHolePunchedUdp` |
| C3 | **The bootstrap relayed data.** `RelayManifestPush` and relay `GetManifest` made it store and serve (unverified) manifests; the relay path only activated for hostnames containing "bootstrap". | Removed `RelayManifestPush`, `TargetUserId`, the `relay` capability, `relayedManifestProvider` and the bootstrap's manifest store. Proto numbers are `reserved`; old peers sending them get `Acknowledged = false`. The bootstrap no longer registers peers from pushes (only from `Announce`). | `Bootstrap_NeverStoresOrServesManifests` |
| C4 | **Symmetric NAT on both sides.** | Accepted, not relayed: if no introducer can connect two peers, `PrepareConnectionAsync` reports it and the download uses any other peer that holds the hash. The NAT guidance text no longer promises a relay. Small items still need S2 (gossip) to reach such pairs; see below. | — |
| C5 | **Announced port after UPnP.** The mapped port was assumed equal to the local one, failures were ignored, and nothing checked reachability. | `NatTraversalService.ExternalPort` records the public port the router actually mapped (null if mapping failed); the router's external IP is only used when a mapping exists. On every `Announce` with a port, the receiver **dials back** to the observed address and that port (a one-shot `Ping`, 3 s timeout, cached 1 min) and registers the peer as outbound-only (port 0) if it fails. The response carries `ObservedAddress` and `DialBackSucceeded`. The peer then shares port 0 in its own record (`IsPubliclyReachable = false`) but keeps announcing its candidate port to bootstrap nodes, so it recovers as soon as the port opens. | `PeerSessionTests.Announce_WithUnreachablePort_*`, `Announce_WithReachablePort_*` |
| C6 | **Routing-table liveness and address authenticity.** PEX mentions refreshed `LastSeen`, keyless PEX entries could overwrite a known peer's address, and the 5-minute cutoff equalled the 5-minute re-bootstrap interval. | Peers sign their own `PeerInfo` (UserId, address, port, `SignedAtUtc`; `PeerRecords`), re-signed every 4 minutes and shared in hellos, announcements and pushes. `PeerRouter` separates **direct** contact (announce, session handshake and traffic, successful fetch/push/PEX, LAN discovery: refresh `LastSeen`, observed address wins) from **hearsay** (PEX and bootstrap lists: may add an unknown peer, but only a validly signed, newer record may change a known peer's address or move its `LastSeen`, and only up to its signing time). Relaying nodes keep the owner's signature only while it still matches the address and port they forward. The liveness cutoff is 12 minutes (`PeerLivenessTimeoutMinutes`), longer than the bootstrap heartbeat; the bootstrap also treats a live control session as liveness. | `PeerRouterLivenessTests` |

### Deviations from the proposed fixes, and follow-ups

- **TCP stays for peers with an open port.** The proposal was one UDP socket for all P2P traffic. Here UDP carries only hole-punched sessions; peers with an open port keep TCP (and one-shot TCP stays for backward compatibility and bulk content from dialable peers). The UDP socket uses the same port number as TCP, and UPnP maps both.
- **No transport encryption yet.** Sessions (TCP and UDP) are authenticated (challenge-response on the `Hello`) but not encrypted, the same as the previous one-shot TCP. All manifest data is signed, so integrity is covered; confidentiality is not. A follow-up could derive a session key from an ephemeral key exchange during the `Hello` and encrypt frames (for UDP, as a LiteNetLib `PacketLayerBase`).
- **IPv6 and LAN-first candidates.** LiteNetLib's introduction already tries the private (LAN) endpoint alongside the public one. IPv6 is not enabled on the UDP socket.
- **Session content is capped at 1 MB per response** (`MaxSessionContentSliceBytes`); chunked downloads (512 KB) are unaffected. Whole-file one-shot requests only use TCP to a dialable peer.
- **Tests run on 127.0.0.1**, where punching trivially succeeds. The protocol path is exercised end to end (introducer, tokens, punch, connect, handshake), but real NAT behaviour still needs H3.

## Part 3: Out of scope, to be addressed in later sessions

Ordered roughly by priority. Each item can be done in its own session.

### Scalability of manifests

#### S1. Push deltas, not whole manifests
**Problem.** Every like or comment triggers `PersistAndFanoutLocalManifest`, which pushes the author's whole stream (snapshot plus 100–500 ops) to every dialable peer in the routing table, one after another. With RSA-4096 each op is about 0.8–1 KB, so a single like to 50 peers is on the order of 10–20 MB of upload. Receivers re-verify every signature each time. `SecurityLimits.ManifestPushCooldownMs` is defined but unused.

**Fix.** Push only new ops (from the receiver's last acknowledged sequence), or announce `{author, stream, headSeq, headHash}` and let receivers pull. Debounce and batch fan-out using the cooldown.

#### S2. Store-and-forward gossip
**Problem.** Peers only serve their own manifests. To see all comments on a track you need a live, direct connection to every commenter. This also matters for C4: two peers that cannot punch through to each other never see each other's updates.

**Fix.** Signed ops verify themselves, so every peer should serve every op it holds. Push to a bounded set of peers (6–8) and let the rest pull lazily; deduplicate by op ID. Secure Scuttlebutt's EBT (per-feed version vectors over append-only signed feeds) is very close to MeshWave's model. GossipSub is the libp2p equivalent.

#### S3. Topic-based replication
**Problem.** Replication is per author, and the routing table is capped at 500 peers, so comments from anyone outside that set are invisible.

**Fix.** Replicate by topic (track, artist, group) so interested peers get a topic's ops from whoever has them. This fits ADR 0001's hybrid model.

#### S4. Efficient anti-entropy
**Fix.** Exchange compact heads summaries (author → head seq + head hash). For large sets, use range-based set reconciliation (e.g. Negentropy) or Bloom filters instead of per-peer, per-stream fetches.

#### S5. Unbounded snapshots and the message-size ceiling
**Problem.**
- Comments (and now group posts, F4) are kept in `Snapshot.PersistentOperations` forever.
- A new peer is always sent the full snapshot.
- Around 2,000 lifetime comments exceeds `SecurityLimits.MaxMessageBytes` (2 MB), after which that user's stream can never be synced by new peers.

**Fix.**
- Move comments and posts into per-topic logs with a retention policy.
- Keep snapshots to aggregated state.
- Page large responses instead of one framed message.

#### S6. Consumers ignore snapshot data
**Problem.** `CompetitionTallyService` and the `GroupMessageReceived`/`GroupStateChanged` events only read `Manifest.Operations`. Anything compacted into `Snapshot.PersistentOperations` (competition ops, and now group ops from F4) is invisible to them after compaction.

**Fix.** Read persistent operations too, via a shared helper that enumerates "snapshot persistent ops + live ops".

#### S7. Smaller, faster signatures and a hash-linked log
**Problem.**
- RSA-4096 signatures are 684 base64 characters per op, dwarfing a like's payload.
- Ops are linked only by sequence number, so an author can sign two different op #N and different peers will silently diverge (no fork detection).

**Fix.**
- Switch to Ed25519 (64-byte signatures) stored as raw bytes, which needs a key-migration plan because `UserId` is derived from the key.
- Add `prevHash` to each op and reject or flag forks.

#### S8. Storage
**Problem.** `PeerManifestStore.SaveToDisk` rewrites a peer's whole JSON file on every merge.

**Fix.** Append-only storage (e.g. SQLite) indexed by author, stream and sequence.

### Content transfer

#### T1. Serving content hashes the entire library per request
**Problem.** `ApplicationViewModel.TryGetLocalContentByHash` walks the library and computes the SHA-256 of every file until it finds a match, then loads the whole file into memory. This happens for **every chunk request** (a 10 MB file is 20+ requests).

**Fix.** Keep a hash → path index (the library cache already stores `ContentHash`). Serve ranges from a `FileStream` instead of `byte[]`, and change `contentProvider` to return a stream or a range.

#### T2. Streaming downloads are not hash-verified
**Problem.** F2 only covers `RequestContentAsync`. `RequestContentStreamAsync`/`ParallelChunkStream` hand out bytes before the whole file is known.

**Fix.** Publish per-chunk hashes (or a Merkle root) in the track's `Create` op so each chunk can be verified on arrival.

#### T3. Remove the stub `ContentExchange` class
`MeshWave.Synchronizer/ContentExchange.cs` is an unused set of TODO stubs; the real transfer lives in `ManifestExchangeClient`/`ManifestExchangeServer`. Remove it, or move content transfer into it.

### Hardening and testing

#### H1. Unused rate limits
`SecurityLimits.MaxConnectionsPerMinutePerIp` and `ManifestPushCooldownMs` are defined but never enforced. Enforce them in `ManifestExchangeServer.AcceptLoopAsync` and the fan-out.

#### H2. Windows-only view-model tests
`MeshWave.ViewModels.Tests` (net10.0-windows) could not be run while making these fixes. They use `MeshTestContext.ConnectAndSyncAllAsync`, which now requires real convergence (F8). Run them on Windows and fix any test that relied on the old forced pushes.

#### H3. NAT-realistic test harness
All integration tests run on 127.0.0.1, where every peer can reach every other. Outbound-only peers (F7) and introductions between them (C2) are covered on loopback, but NAT itself is not simulated. Use Linux network namespaces with iptables MASQUERADE in CI so hole punching (including a symmetric-NAT pair, C4) is exercised for real.
