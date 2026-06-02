# ADR 0001: Shared Catalogue Search and Replication Strategy

## Status
Proposed

## Context
MeshWave currently uses an append-only signed log (manifest) architecture where peers exchange full lists of operations to synchronize state. As the network grows, replicating every manifest to every peer (Full Replication) becomes unsustainable due to bandwidth and storage constraints. Conversely, a purely Distributed Search model (sending queries to the mesh on-demand) may suffer from high latency and unreliable results if many peers are offline or behind restrictive NATs.

We need to decide on a strategy for the "Shared Catalogue" — how users discover music and artists they don't already follow.

## Decision
We will adopt a **Hybrid Model** that combines proactive metadata replication with reactive distributed search.

### 1. Social Replication (High Fidelity)
Peers will automatically replicate and keep synchronized the full manifests of:
- Themselves (Local Manifest).
- Peers they explicitly "Follow".
- Peers they have "Friended".
- Groups they have joined.

This ensures that the user's core "Library" and "Feed" are always available instantly and offline.

### 2. Selective Discovery Replication (Medium Fidelity)
Peers will maintain a limited-size "Discovery Cache" of manifests from:
- Peers encountered via PEX (Peer Exchange) who have an `IsArtist` flag set.
- Trending or highly-connected peers (based on "Likes" or "Follow" counts seen in other manifests).

This cache is subject to eviction policies (e.g., Least Recently Seen) to bound local storage.

### 3. Distributed Search (On-Demand)
For global search queries that cannot be satisfied by the local replicated catalogue:
- The client will broadcast a `SearchRequest(Query)` to connected peers.
- Peers will respond with matching metadata from their own local manifests or their discovery caches.
- Results are aggregated and de-duplicated on the requester's side.

## Consequences

### Positive
- **Performance**: Instant results for the user's followed artists and social circle.
- **Reliability**: Social content is available offline.
- **Scalability**: Limits the total amount of metadata any single peer must store, as they only replicate the "whole mesh" if they choose to follow everyone (which is capped by security limits).

### Negative
- **Complexity**: Requires implementing both a synchronization engine and a distributed query protocol.
- **Inconsistency**: Search results for the wider mesh may vary depending on which peers are currently online and reachable.

## Alternatives Considered

### Full Replicated Metadata Index
- **Pros**: Global search is instant and complete.
- **Cons**: Bandwidth and storage grow linearly with the total number of users and tracks in the entire mesh. Unfeasible for a truly decentralized system at scale.

### Purely Distributed Search (Gnutella-style)
- **Pros**: Zero storage overhead for non-followed content.
- **Cons**: Search is slow (network latency). Results are incomplete if the "path" to the content is offline. Poor user experience for basic browsing.

## Performance Considerations
- **Index Optimization**: The local replicated manifests must be indexed in a lightweight local database (e.g., SQLite or a specialized file-based index) to allow fast tokenized search across thousands of tracks.
- **Search Throttling**: Distributed search requests must be rate-limited to prevent mesh-wide DoS.
- **Bloom Filters**: Future optimization could use Bloom filters or Graphene-style sync to check for updates without exchanging full manifests.
