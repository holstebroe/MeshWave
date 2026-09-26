# MeshWave P2P Handshake and NAT Traversal

This document defines how MeshWave establishes direct peer connectivity before asking for manual router configuration.

## Goals

- Prefer direct peer-to-peer transfer (no central content server)
- Only one node (a peer or a standalone bootstrap) needs an open port
- The bootstrap only helps peers find and reach each other; it never relays data
- Provide deterministic fallback steps and actionable user guidance

## Reachability

On startup a peer with a listener tries to map its port with UPnP/NAT-PMP (`NatTraversalService`) and announces the
mapped external port to every bootstrap node. The bootstrap dials back to the observed address and announced port;
if that fails, the peer is registered, and shares its own record, as outbound-only (port 0). It keeps announcing the
real port on every heartbeat, so it becomes dialable again once the port opens.

## Persistent sessions

Every peer keeps a session (a long-lived, multiplexed connection) with each bootstrap node and with a few neighbours.
Pushes and requests travel in both directions over it, so peers without an open port receive updates as soon as they
happen. Sessions start with a mutual `Hello` challenge-response, so a session is only attributed to a UserId after the
remote proved it holds that user's key. See [P2P-Exchange-Protocol.md](P2P-Exchange-Protocol.md#persistent-sessions).

## Hole punching (introductions)

When peer B wants to reach peer C and neither has an open port:

1. B asks an introducer A it has a session with (`RequestIntroduction`). A must also have a session with C.
2. A issues a single-use token and forwards an `IntroductionOffer` to C.
3. B and C both send NAT introduce requests with the token to A's UDP port (the same port number as its TCP port).
   A observes their public UDP endpoints, like STUN.
4. A sends each of them the other's public and private endpoint (LiteNetLib `NatPunchModule`).
5. B and C punch and connect at the same time. Only connections carrying an expected token are accepted.
6. The UDP connection (reliable, ordered) becomes a normal session, starting with the `Hello` handshake.

Peers attempt this automatically for peers without an open port, and again on demand before a download.

## Ordered connection attempts for content

When a peer requests content from another peer, `SyncOrchestrator.PrepareConnectionAsync` tries:

1. **Routing table resolution**, with a bootstrap refresh (`GetPeers`) if the peer is unknown.
2. **Persistent session**: use it if one is open.
3. **Direct TCP probe**, if the peer has an open port.
4. **UDP introduction**, as above.
5. **Fallback: user-facing NAT guidance**, with the local IP and port and the remote endpoint.

Downloads use every peer that holds the content hash and can be reached, not just the original publisher.

## Symmetric NAT on both sides

Two peers behind symmetric NATs usually cannot punch through to each other. MeshWave accepts this rather than
relaying through the bootstrap: content comes from other peers that hold it. Getting small items (comments, likes)
to such pairs needs store-and-forward gossip (item S2 in [P2P-Protocol-Review.md](P2P-Protocol-Review.md)).

## Security Notes

- Keep all limits enforced via `SecurityLimits`
- Keep bootstrap endpoint parsing strict (`host:port`)
- Avoid accepting oversized peer lists or malformed endpoints
- Routing metadata is only trusted from direct contact or the peer's own signed record; continue manifest signature verification
- The dial-back only ever connects to the requester's own observed address
- Sessions are authenticated but not yet encrypted; all manifest data is signed
