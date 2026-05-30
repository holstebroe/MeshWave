# MeshWave P2P Handshake and NAT Traversal

This document defines how MeshWave attempts to establish direct peer connectivity before asking for manual router configuration.

## Goals

- Prefer direct peer-to-peer transfer (no central content server)
- Keep bootstrap traffic minimal (metadata only)
- Provide deterministic fallback steps and actionable user guidance

## Ordered Connection Attempt Pipeline

When a peer requests content from another peer, MeshWave uses this sequence:

1. **Routing table resolution**
   - Find target peer by `UserId` in `PeerRouter`.
   - Sources are LAN discovery, previously learned PEX entries, and bootstrap updates.

2. **Bootstrap refresh (if target missing)**
   - Query configured bootstrap nodes (`host:port`) for current peers (`GetPeers`).
   - Merge returned peers into router table.
   - Retry target resolution.

3. **Direct TCP probe**
   - Attempt short-timeout TCP connect to the peer manifest endpoint.
   - This is a cheap reachability signal and helps diagnostics.

4. **UDP hole punching**
   - Send multiple UDP punch probes with nonce.
   - Accept and respond with ACK packets.
   - If ACK is received, NAT path is likely open in both directions.

5. **Direct content request**
   - Attempt peer content retrieval over the discovered endpoint.
   - If transfer succeeds, no further action required.

6. **Fallback: user-facing NAT guidance**
   - If all methods fail, present concrete routing suggestions:
	 - local private IP and local manifest/content port
	 - remote endpoint used during failed attempts
	 - protocol recommendation (TCP + UDP)

## Bootstrap Bandwidth Policy

Bootstrap is designed to stay lightweight:

- Bootstrap serves only peer metadata (PEX), not file payloads
- Bootstrap does not become a central relay in normal mode
- Peer-to-peer direct transfer remains the default path

## Bootstrap Rendezvous / "Crossing Hands"

A known NAT traversal strategy is to use a public coordinator for rendezvous timing,
where both peers attempt outbound traffic in a coordinated window.

MeshWave currently supports:

- bootstrap peer list refresh
- UDP punch/ack exchange

Future enhancement candidates:

- explicit rendezvous session ID issued by bootstrap
- coordinated simultaneous SYN/UDP probes to improve symmetric-NAT success
- optional relay fallback only when direct methods fail

## User Guidance Requirements

When fallback is reached, UI messaging should include:

- which connection methods were attempted and their result
- exact local endpoint recommendation (`<local-ip>:<port>`)
- exact remote endpoint involved
- short plain-language explanation of why manual forwarding may be required

## Security Notes

- Keep all limits enforced via `SecurityLimits`
- Keep bootstrap endpoint parsing strict (`host:port`)
- Avoid accepting oversized peer lists or malformed endpoints
- Do not trust routing metadata for identity; continue manifest signature verification
