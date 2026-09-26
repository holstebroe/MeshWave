# P2P Protocol Review

Review of the MeshWave network protocol against its design goals:

- **Serverless mesh.** Peers exchange signed manifests and files directly.
- **Minimal open ports.** Only one peer (or a standalone bootstrap) needs an open port. The bootstrap's only job is establishing direct connections (discovery, hole-punching); it must never relay data.
- **Scales to many small items** (comments, likes) and many peers.

This document lists what was fixed in the first pass (quick fixes) and what remains, with a proposed fix for each remaining item so it can be picked up in a separate session.

## Part 1: Fixed in this pass

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

## Part 2: Out of scope, to be addressed in later sessions

Ordered roughly by priority. Each item can be done in its own session.

### Connectivity (the core design gap)

#### C1. Persistent, bidirectional peer connections
**Problem.** Every exchange opens a new TCP connection from the requester to the target's listening port. A peer behind NAT can only make requests; it can never be pushed to. F7 works around this by polling every 60 s, which is slow for comments and costs about 3 connections per peer per minute.

**Fix.** Keep one long-lived connection per neighbour and multiplex requests over it in both directions (frames carry a request ID and a direction). Whoever can dial, dials; once connected, both sides can push and request. Send a keepalive about every 25 s to hold NAT mappings open. Then remove the periodic poll (or reduce it to an anti-entropy fallback every few minutes). Files: `ManifestExchangeClient`, `ManifestExchangeServer`, `SyncOrchestrator`, `PeerRouter`.

#### C2. Real rendezvous and hole punching (B↔C through A)
**Problem.** The hole punching that exists does nothing useful:
- The bootstrap has no UDP socket, so it never learns anyone's public UDP address and port.
- `BootstrapCoordinator.OnRendezvousRequested` stores a session but never notifies the target and exchanges no addresses.
- `NatTraversalService.TryPunchAsync` punches UDP towards the peer's TCP port, but all traffic afterwards is TCP.
- `PrepareConnectionAsync` only runs when the peer is missing from the routing table.

**Fix.**
1. Every peer keeps a control connection (C1) to at least one introducer. Any peer with an open port can be one; fold `BootstrapCoordinator` features into normal peers.
2. Use one UDP socket per peer for all P2P traffic. The introducer observes each peer's public address and port on it (like STUN) and sends an "introduce" message to **both** B and C with each other's public and private addresses.
3. Both sides punch at the same time, then run a reliable, encrypted transport over the same socket.

LiteNetLib (`NatPunchModule` + introducer) implements this pattern in .NET. Try LAN (private) and IPv6 addresses first. TCP simultaneous-open is not reliable enough to rely on.

#### C3. Remove the bootstrap data relay
**Problem.** `RelayManifestPush` and relay `GetManifest` (`TargetUserId`) make the bootstrap store and serve manifests. That contradicts the design, and the implementation is broken too:
- The relay fetch path only activates if the bootstrap's hostname contains the word "bootstrap" (`ManifestExchangeClient.FetchManifestAsync`).
- The bootstrap stores relayed manifests without verifying them, so anyone can overwrite a user's relayed manifest.

**Fix.** Once F7/C1 give outbound-only peers a working path, delete `RelayManifestPush`, the `relay` capability and `relayedManifestProvider`. Update the header comment in `MeshWave.Bootstrap/Program.cs`.

#### C4. Symmetric NAT on both sides
**Problem.** Two peers behind symmetric NATs usually cannot punch through to each other.

**Fix.** Accept it, rather than relaying through the bootstrap. With gossip replication (S2), small items don't need a direct B↔C link, and files can come from any other peer that holds the hash. Optionally, peers (not the bootstrap) can volunteer as relays with user consent later.

#### C5. Announced address and port after UPnP
**Problem.** `NatTraversalService.SetupPortMappingAsync` maps external port = internal port and ignores failures. `BuildAnnouncingPeerInfo` always announces the local port.

**Fix.** Announce the mapped external port. If mapping fails and the peer is not reachable, fall back to outbound-only (port 0) automatically. Detect reachability with a dial-back check by the bootstrap after `Announce`.

#### C6. Routing-table liveness and address authenticity
**Problem.**
- `PeerRouter.AddOrRefreshPeer` refreshes `LastSeen` from any PEX mention, so peers nobody has actually reached look online.
- Keyless PEX entries can still overwrite a known peer's address (F5 only protects the key).
- The bootstrap entry's 5-minute cutoff equals the 5-minute re-bootstrap interval, so it can flicker out of `GetPeers()`.

**Fix.**
- Only refresh `LastSeen` on direct contact.
- Make peers sign their own `PeerInfo` records (address, port, timestamp) and ignore unsigned updates to known peers.
- Make the liveness cutoff longer than the re-bootstrap interval.

### Scalability of manifests

#### S1. Push deltas, not whole manifests
**Problem.** Every like or comment triggers `PersistAndFanoutLocalManifest`, which pushes the author's whole stream (snapshot plus 100–500 ops) to every dialable peer in the routing table, one after another. With RSA-4096 each op is about 0.8–1 KB, so a single like to 50 peers is on the order of 10–20 MB of upload. Receivers re-verify every signature each time. `SecurityLimits.ManifestPushCooldownMs` is defined but unused.

**Fix.** Push only new ops (from the receiver's last acknowledged sequence), or announce `{author, stream, headSeq, headHash}` and let receivers pull. Debounce and batch fan-out using the cooldown.

#### S2. Store-and-forward gossip
**Problem.** Peers only serve their own manifests (`relayedManifestProvider` returns null in `SyncOrchestrator`). To see all comments on a track you need a live, direct connection to every commenter.

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
All integration tests run on 127.0.0.1, where every peer can reach every other. Add tests that simulate NAT (outbound-only peers, as in F7). For C2, use Linux network namespaces with iptables MASQUERADE in CI, so hole punching is exercised for real.
