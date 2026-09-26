using System.Collections.Concurrent;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;

namespace MeshWave.Synchronizer;

/// <summary>
/// Manifest replication. Streams are append-only logs of individually signed, hash-linked operations, so they travel as
/// deltas and any peer can store and forward them:
/// <list type="bullet">
/// <item>Local operations are pushed as a delta (only the operations each neighbour lacks), debounced by
/// <see cref="SecurityLimits.FanoutDebounceMs"/> so that bursts go out as one message.</item>
/// <item>Operations received by push are forwarded to <see cref="SecurityLimits.GossipFanout"/> neighbours (store-and-forward
/// gossip); a peer that already has them merges nothing and stops the flood.</item>
/// <item>Anti-entropy compares stream heads with a neighbour (<see cref="ManifestRequestType.GetHeads"/>) and pulls, page by
/// page, only the streams where it is behind, from whichever neighbour has them.</item>
/// </list>
/// </summary>
public partial class SyncOrchestrator
{
    /// <summary>Largest number of operations a forwarded (gossiped) delta may carry; bigger catch-ups travel by anti-entropy.</summary>
    private const int MaxGossipForwardOperations = 64;

    /// <summary>Maximum number of pages exchanged for one stream in one push or pull.</summary>
    private const int MaxPagesPerTransfer = 64;

    /// <summary>What each neighbour is known to hold: (neighbour, author, stream) → head sequence number.</summary>
    private readonly ConcurrentDictionary<(string Peer, string Author, ManifestStreamType Stream), int> _peerHeads = new();

    /// <summary>Local streams with operations that have not been pushed yet.</summary>
    private readonly ConcurrentDictionary<ManifestStreamType, byte> _dirtyStreams = new();

    /// <summary>Head of each local stream when it was last pushed; a neighbour whose head is unknown gets the operations after it.</summary>
    private readonly ConcurrentDictionary<ManifestStreamType, int> _lastFanoutHeads = new();

    private readonly ConcurrentDictionary<string, DateTime> _reportedForks = new(StringComparer.Ordinal);
    private int _fanoutScheduled;

    /// <summary>Any stream this node holds: its own, or one it replicates for another author.</summary>
    private Manifest? GetStream(string authorUserId, ManifestStreamType streamType)
    {
        return string.Equals(authorUserId, Identity?.UserId, StringComparison.OrdinalIgnoreCase)
            ? GetLocalManifest(streamType)
            : _peerStore.Get(authorUserId, streamType);
    }

    /// <summary>Heads of every stream this node holds, its own first.</summary>
    private IReadOnlyList<StreamHead> GetAllHeads()
    {
        var heads = new List<StreamHead>();
        foreach (var manifest in _localManifests.Values)
            heads.Add(ManifestManager.GetHead(manifest));
        foreach (var manifest in _peerStore.GetAll())
        {
            var head = ManifestManager.GetHead(manifest);
            if (head.HeadSequenceNumber >= 0) heads.Add(head);
        }
        return heads.Take(SecurityLimits.MaxHeadsPerExchange).ToList();
    }

    // ─── Local operations: debounced delta fan-out ───────────────────────────

    /// <summary>Marks a local stream as changed; its new operations are pushed after <see cref="SecurityLimits.FanoutDebounceMs"/>.</summary>
    private void ScheduleFanout(ManifestStreamType streamType)
    {
        _dirtyStreams[streamType] = 0;
        if (Interlocked.Exchange(ref _fanoutScheduled, 1) != 0) return;

        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SecurityLimits.FanoutDebounceMs, ct);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _fanoutScheduled, 0);
                return;
            }
            Interlocked.Exchange(ref _fanoutScheduled, 0);
            await FlushFanoutAsync(ct);
        });
    }

    private async Task FlushFanoutAsync(CancellationToken ct)
    {
        var self = Identity?.UserId;
        if (self == null) return;

        var pushes = new List<Task>();
        foreach (var streamType in _dirtyStreams.Keys.ToList())
        {
            if (!_dirtyStreams.TryRemove(streamType, out _)) continue;
            var manifest = GetLocalManifest(streamType);
            if (manifest == null) continue;

            var head = ManifestManager.GetHeadSequenceNumber(manifest);
            var from = _lastFanoutHeads.GetValueOrDefault(streamType, -1) + 1;
            _lastFanoutHeads[streamType] = head;

            var targets = SelectTargets(self, excludeUserId: null, ownStream: true);
            _logger.Debug("Pushing local {0} delta (from seq {1} to {2}) to {3} peer(s).", streamType, from, head, targets.Count);
            pushes.AddRange(targets.Select(peer => PushStreamAsync(peer, self, streamType, from, ct)));
        }
        await Task.WhenAll(pushes);
    }

    /// <summary>
    /// Peers to push a stream to. Our own operations go to every peer with a session plus a few dialable ones; forwarded
    /// operations go to <see cref="SecurityLimits.GossipFanout"/> peers, preferring those with a session.
    /// </summary>
    private List<PeerInfo> SelectTargets(string authorUserId, string? excludeUserId, bool ownStream)
    {
        var candidates = _router.GetPeers()
            .Where(p => !IsBootstrapEntry(p) && HasRoute(p))
            .Where(p => !string.Equals(p.UserId, authorUserId, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p.UserId, excludeUserId, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p.UserId, Identity?.UserId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var withSession = candidates.Where(p => _sessions.GetSession(p.UserId) != null).ToList();
        var others = candidates.Where(p => _sessions.GetSession(p.UserId) == null);

        if (ownStream)
            return withSession.Concat(others.Take(SecurityLimits.GossipFanout)).ToList();
        return withSession.Concat(others).Take(SecurityLimits.GossipFanout).ToList();
    }

    /// <summary>
    /// Sends <paramref name="peer"/> the operations of a stream it lacks. If its head is unknown, starts at
    /// <paramref name="defaultFrom"/>; the peer answers with its head, and when the delta did not connect to what it holds,
    /// the missing range (from the snapshot or the very start, including the author's key, if need be) is sent next.
    /// </summary>
    private async Task PushStreamAsync(PeerInfo peer, string authorUserId, ManifestStreamType streamType, int defaultFrom, CancellationToken ct)
    {
        var source = GetStream(authorUserId, streamType);
        if (source == null) return;

        var key = (peer.UserId, authorUserId, streamType);
        var sourceHead = ManifestManager.GetHeadSequenceNumber(source);
        var from = _peerHeads.TryGetValue(key, out var known) ? known + 1 : Math.Max(0, defaultFrom);

        for (var round = 0; round < MaxPagesPerTransfer && from <= sourceHead; round++)
        {
            var page = ManifestManager.BuildPage(source, from);
            if (page.Operations.Count == 0 && page.Snapshot == null) return;
            var lastSent = page.Operations.Count > 0 ? page.Operations[^1].SequenceNumber : page.Snapshot!.LastSequenceNumber;

            PushResult result;
            try
            {
                result = await _client.PushStreamAsync(peer, page, BuildPushSender(peer, streamType), ct);
            }
            catch (Exception ex)
            {
                RecordPeerMessage(peer.UserId, "PushManifest", success: false,
                    $"Push of {authorUserId} {streamType} failed via {DescribeRoute(peer)}: {ex.Message}");
                return;
            }

            RecordPeerMessage(peer.UserId, "PushManifest", success: result.Acknowledged,
                $"Pushed {(authorUserId == Identity?.UserId ? "local" : authorUserId)} {streamType} delta ({page.Operations.Count} op from seq {from}{(page.Snapshot != null ? ", with snapshot" : string.Empty)}) via {DescribeRoute(peer)}.");
            if (!result.Acknowledged) return;
            _router.MarkContacted(peer.UserId);

            var theirs = result.ReceiverHead?.HeadSequenceNumber ?? lastSent;
            _peerHeads[key] = theirs;
            if (theirs >= sourceHead) return;

            int next;
            if (theirs < from - 1) next = theirs + 1;               // the delta did not connect: send what they lack
            else if (theirs >= lastSent && page.HasMore) next = theirs + 1; // next page
            else return;                                            // rejected (fork, limits) - nothing more to do

            if (next == from) return;
            from = next;
        }
    }

    /// <summary>
    /// Identifies the sender of a push. Over an authenticated session the receiver already knows who we are (and our key),
    /// so only the UserId is sent: the full signed record would triple the size of a single like.
    /// </summary>
    private PeerInfo BuildPushSender(PeerInfo peer, ManifestStreamType streamType)
    {
        var session = _sessions.GetSession(peer.UserId);
        if (session?.IsAuthenticated == true && Identity != null)
            return new PeerInfo { UserId = Identity.UserId, DisplayName = string.Empty, Address = string.Empty };
        return BuildAnnouncingPeerInfo(streamType);
    }

    // ─── Received operations: merge, notify, forward ─────────────────────────

    /// <summary>
    /// Merges a received page of <paramref name="remote"/>'s author stream, raises events for the new operations, and
    /// (for pushed, fresh operations) forwards them to a few neighbours. Returns how far the stored head advanced.
    /// </summary>
    private int TryMerge(Manifest remote, string publicKeyPem, string? sourcePeerUserId = null, bool forward = false)
    {
        if (remote.UserId == Identity?.UserId) return 0;

        _logger.Debug("Attempting merge of manifest from peer {0} ({1} ops, stream={2})", remote.UserId, remote.Operations.Count, remote.StreamType);
        var previousHead = ManifestManager.GetHeadSequenceNumber(_peerStore.Get(remote.UserId, remote.StreamType));

        _peerStore.MergeAndSave(remote, publicKeyPem, _manifestManager);
        var stored = _peerStore.Get(remote.UserId, remote.StreamType);
        var added = Math.Max(0, ManifestManager.GetHeadSequenceNumber(stored) - previousHead);

        if (sourcePeerUserId != null && stored != null)
        {
            var sentUpTo = remote.Operations.Count > 0 ? remote.Operations.Max(o => o.SequenceNumber) : remote.Snapshot?.LastSequenceNumber ?? -1;
            _peerHeads.AddOrUpdate((sourcePeerUserId, remote.UserId, remote.StreamType), sentUpTo, (_, h) => Math.Max(h, sentUpTo));
        }

        if (added <= 0 || stored == null)
        {
            _logger.Trace("Merge of manifest from peer {0} resulted in 0 new operations.", remote.UserId);
            return 0;
        }

        _logger.Info("Merged {0} manifest of {1}: added {2} new operations.", remote.StreamType, remote.UserId, added);

        List<ManifestOperation> newOps;
        lock (stored)
        {
            newOps = stored.AllOperations().Where(op => op.SequenceNumber > previousHead).OrderBy(op => op.SequenceNumber).ToList();
        }
        OnNewOperations(remote, stored, newOps);

        if (forward && added <= MaxGossipForwardOperations)
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var targets = SelectTargets(remote.UserId, sourcePeerUserId, ownStream: false);
            _ = Task.Run(() => Task.WhenAll(targets.Select(peer => PushStreamAsync(peer, remote.UserId, remote.StreamType, previousHead + 1, ct))), ct);
        }

        ManifestMerged?.Invoke(this, new ManifestMergedEventArgs(remote.UserId, added));
        return added;
    }

    /// <summary>Profile, icon, catalogue and group side effects of newly merged operations.</summary>
    private void OnNewOperations(Manifest remote, Manifest stored, IReadOnlyList<ManifestOperation> newOps)
    {
        var profileOp = newOps.LastOrDefault(op => op.OperationType == ManifestOperationType.Profile);
        var profileMetadata = profileOp?.Metadata;
        var profileIcon = profileOp?.ContentHash;
        if (profileOp == null && ReferenceEquals(stored.Snapshot, remote.Snapshot) && remote.Snapshot != null)
        {
            // A newly adopted snapshot carries the latest profile as an entity state.
            var profileState = remote.Snapshot.EntityStates.FirstOrDefault(e => e.TargetType == "User" && e.TargetId == remote.UserId);
            profileMetadata = profileState?.Metadata;
            profileIcon = profileState?.ContentHash;
        }

        if (profileMetadata != null)
        {
            _logger.Debug("Updating profile for {0} from merged manifest", remote.UserId);
            UserRepository?.UpdateProfile(remote.UserId, profileMetadata);
            if (!string.IsNullOrWhiteSpace(profileIcon))
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await RequestContentAsync(remote.UserId, profileIcon);
                        if (bytes != null) UserRepository?.SaveUserIcon(remote.UserId, bytes);
                    }
                    catch { }
                });
        }

        _ = CatalogueService.IngestAsync(remote);

        foreach (var op in newOps)
        {
            if (op.OperationType is not (ManifestOperationType.Create or ManifestOperationType.Update)) continue;

            var iconHash = op.Metadata.GetValueOrDefault("iconHash");
            if (string.IsNullOrWhiteSpace(iconHash) && op.TargetType == "User")
                iconHash = op.ContentHash;
            if (string.IsNullOrWhiteSpace(iconHash) && op.Metadata.GetValueOrDefault("isIcon") == "True")
                iconHash = op.ContentHash;
            if (string.IsNullOrWhiteSpace(iconHash)) continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    var bytes = await RequestContentAsync(remote.UserId, iconHash);
                    if (bytes != null && UserRepository != null) UserRepository.SaveUserIcon(op.TargetId, bytes);
                }
                catch { }
            });
        }

        foreach (var op in newOps)
        {
            if (op.OperationType == ManifestOperationType.PostMessage)
            {
                GroupMessageReceived?.Invoke(this, new GroupMessageEventArgs(
                    remote.UserId,
                    op.Metadata.GetValueOrDefault("channelId") ?? string.Empty,
                    op.TargetId,
                    op.Metadata.GetValueOrDefault("content") ?? string.Empty,
                    op.Metadata.GetValueOrDefault("parentPostId")));
            }
            else if (op.OperationType is ManifestOperationType.CreateChannel or ManifestOperationType.FoundGroup or ManifestOperationType.ModerateGroup
                     or ManifestOperationType.GroupJoin or ManifestOperationType.GroupLeave)
            {
                GroupStateChanged?.Invoke(this, new GroupStateChangedEventArgs(remote.UserId, op.OperationType, op.TargetId, op.Metadata));
            }
        }
    }

    /// <summary>
    /// The author's public key, from the streams we already hold, the routing table, the sender (if it is the author),
    /// the manifest itself or its profile operation. Only a key the author's UserId was derived from is returned.
    /// </summary>
    private string? ResolveAuthorKey(string authorUserId, Manifest? incoming, PeerInfo? sender)
    {
        var stored = Enum.GetValues<ManifestStreamType>()
            .Select(s => _peerStore.Get(authorUserId, s)?.AuthorPublicKey)
            .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
        var routed = _router.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, authorUserId, StringComparison.OrdinalIgnoreCase))?.PublicKeyPem;
        var fromSender = sender != null && string.Equals(sender.UserId, authorUserId, StringComparison.OrdinalIgnoreCase) ? sender.PublicKeyPem : null;
        var fromProfile = incoming?.AllOperations()
            .Where(op => op.OperationType == ManifestOperationType.Profile)
            .Select(op => op.Metadata.GetValueOrDefault("publicKeyPem"))
            .LastOrDefault(pk => !string.IsNullOrWhiteSpace(pk));

        return new[] { stored, routed, fromSender, incoming?.AuthorPublicKey, fromProfile }
            .FirstOrDefault(pk => CryptoService.IsPublicKeyForUser(authorUserId, pk));
    }

    // ─── Anti-entropy ────────────────────────────────────────────────────────

    /// <summary>
    /// Compares stream heads with <paramref name="peer"/>, pulls the streams where it is ahead (its own and the ones it
    /// replicates) and pushes our own streams where it is behind.
    /// </summary>
    private async Task SyncWithPeerAsync(PeerInfo peer, CancellationToken ct)
    {
        if (!CryptoService.IsPublicKeyForUser(peer.UserId, peer.PublicKeyPem)) return;
        if (peer.UserId == Identity?.UserId) return;
        if (!HasRoute(peer)) return;

        IReadOnlyList<StreamHead>? heads;
        try
        {
            heads = await _client.FetchHeadsAsync(peer, ct);
        }
        catch (Exception ex)
        {
            RecordPeerMessage(peer.UserId, "FetchHeads", success: false, $"Heads exchange failed via {DescribeRoute(peer)}: {ex.Message}");
            return;
        }
        if (heads == null)
        {
            RecordPeerMessage(peer.UserId, "FetchHeads", success: false, $"Peer {DescribeRoute(peer)} returned no stream heads.");
            return;
        }

        _router.MarkContacted(peer.UserId);
        Interlocked.Increment(ref _outboundManifestFetchCount);

        var ours = GetAllHeads().ToDictionary(h => (h.UserId, h.StreamType));
        var theirs = new Dictionary<(string, ManifestStreamType), StreamHead>();
        foreach (var head in heads)
        {
            if (string.IsNullOrWhiteSpace(head.UserId) || !Enum.IsDefined(head.StreamType)) continue;
            theirs[(head.UserId, head.StreamType)] = head;
            _peerHeads[(peer.UserId, head.UserId, head.StreamType)] = head.HeadSequenceNumber;

            if (ours.TryGetValue((head.UserId, head.StreamType), out var mine) && mine.HeadSequenceNumber == head.HeadSequenceNumber
                && mine.HeadHash != head.HeadHash && !string.IsNullOrEmpty(mine.HeadHash) && !string.IsNullOrEmpty(head.HeadHash))
                ReportForkOnce(peer, head);
        }

        var self = Identity?.UserId;
        var knownAuthors = ours.Keys.Select(k => k.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toPull = heads
            .Where(h => h.UserId != self && h.HeadSequenceNumber > (ours.TryGetValue((h.UserId, h.StreamType), out var mine) ? mine.HeadSequenceNumber : -1))
            // The peer's own streams first, then authors we already follow, then new ones.
            .OrderBy(h => h.UserId == peer.UserId ? 0 : knownAuthors.Contains(h.UserId) ? 1 : 2)
            .Take(SecurityLimits.MaxStreamsPulledPerRound)
            .ToList();

        RecordPeerMessage(peer.UserId, "FetchHeads", success: true,
            $"Peer {DescribeRoute(peer)} holds {heads.Count} stream(s); pulling {toPull.Count}.");

        foreach (var head in toPull)
        {
            if (ct.IsCancellationRequested) return;
            await PullStreamAsync(peer, head.UserId, head.StreamType, ct);
        }

        // Our own streams: the peer lacks some (e.g. it just joined), or holds more than we do (we lost local data).
        if (self == null) return;
        foreach (var streamType in Enum.GetValues<ManifestStreamType>())
        {
            var mine = GetLocalManifest(streamType);
            if (mine == null) continue;
            var myHead = ManifestManager.GetHeadSequenceNumber(mine);
            var theirHead = theirs.TryGetValue((self, streamType), out var h) ? h.HeadSequenceNumber : -1;
            if (theirHead < myHead)
                await PushStreamAsync(peer, self, streamType, theirHead + 1, ct);
            else if (theirHead > myHead)
                await RecoverOwnStreamAsync(peer, streamType, ct);
        }
    }

    /// <summary>
    /// Pulls operations of our own stream that a neighbour holds and we do not (the local data was lost or restored from
    /// an old backup). Without this, the next local operation would reuse a sequence number the network already has: a
    /// fork, which peers reject.
    /// </summary>
    private async Task RecoverOwnStreamAsync(PeerInfo peer, ManifestStreamType streamType, CancellationToken ct)
    {
        var identity = Identity;
        var local = GetLocalManifest(streamType);
        if (identity == null || local == null) return;

        var recovered = 0;
        for (var page = 0; page < MaxPagesPerTransfer; page++)
        {
            var from = ManifestManager.GetHeadSequenceNumber(local) + 1;
            Manifest? remote;
            try
            {
                remote = await _client.FetchStreamAsync(peer, identity.UserId, streamType, from, ct);
            }
            catch (Exception ex)
            {
                RecordPeerMessage(peer.UserId, "FetchManifest", success: false, $"Recovery of our own {streamType} stream failed: {ex.Message}");
                break;
            }
            if (remote == null || remote.UserId != identity.UserId || remote.StreamType != streamType) break;

            var headBefore = ManifestManager.GetHeadSequenceNumber(local);
            try
            {
                _manifestManager.MergeManifest(local, remote, identity.PublicKeyPem);
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not recover our own {0} stream from {1}: {2}", streamType, peer.UserId, ex.Message);
                break;
            }

            var gained = ManifestManager.GetHeadSequenceNumber(local) - headBefore;
            if (gained <= 0) break;
            recovered += gained;
            if (!remote.HasMore) break;
        }

        if (recovered <= 0) return;
        SaveLocalManifest(local);
        _lastFanoutHeads[streamType] = ManifestManager.GetHeadSequenceNumber(local);
        _logger.Warn("Recovered {0} operations of our own {1} stream from peer {2}.", recovered, streamType, peer.UserId);
        RecordPeerMessage(peer.UserId, "FetchManifest", success: true, $"Recovered {recovered} operation(s) of our own {streamType} stream that we no longer had.");
    }

    /// <summary>Pulls one stream from a neighbour, page by page, from our head onwards.</summary>
    private async Task PullStreamAsync(PeerInfo peer, string authorUserId, ManifestStreamType streamType, CancellationToken ct)
    {
        for (var page = 0; page < MaxPagesPerTransfer; page++)
        {
            var from = ManifestManager.GetHeadSequenceNumber(GetStream(authorUserId, streamType)) + 1;
            Manifest? remote;
            try
            {
                remote = await _client.FetchStreamAsync(peer, authorUserId, streamType, from, ct);
            }
            catch (Exception ex)
            {
                RecordPeerMessage(peer.UserId, "FetchManifest", success: false, $"Fetch of {authorUserId} {streamType} failed: {ex.Message}");
                return;
            }
            if (remote == null || (remote.Operations.Count == 0 && remote.Snapshot == null) || remote.UserId != authorUserId || remote.StreamType != streamType)
                return;

            var key = ResolveAuthorKey(authorUserId, remote, peer);
            if (key == null)
            {
                RecordPeerMessage(peer.UserId, "FetchManifest", success: false, $"No public key for {authorUserId}; cannot verify its {streamType} stream.");
                return;
            }

            var added = TryMerge(remote, key, peer.UserId);
            RecordPeerMessage(peer.UserId, "FetchManifest", success: true,
                $"Fetched {(authorUserId == peer.UserId ? "its" : authorUserId)} {streamType} page ({remote.Operations.Count} op from seq {from}{(remote.Snapshot != null ? ", with snapshot" : string.Empty)}, {added} new) via {DescribeRoute(peer)}.");
            if (added == 0 || !remote.HasMore) return;
        }
    }

    /// <summary>Forgets what a neighbour holds once it leaves the routing table.</summary>
    private void ForgetPeerHeads(string peerUserId)
    {
        foreach (var key in _peerHeads.Keys.Where(k => string.Equals(k.Peer, peerUserId, StringComparison.OrdinalIgnoreCase)).ToList())
            _peerHeads.TryRemove(key, out _);
    }

    private void ReportForkOnce(PeerInfo peer, StreamHead head)
    {
        var key = $"{head.UserId}|{head.StreamType}|{head.HeadSequenceNumber}";
        if (!_reportedForks.TryAdd(key, DateTime.UtcNow)) return;
        if (_reportedForks.Count > 1000) _reportedForks.Clear();

        _logger.Warn("Fork: peer {0} holds a different operation {1} of {2}'s {3} stream than we do.", peer.UserId, head.HeadSequenceNumber, head.UserId, head.StreamType);
        RecordPeerMessage(head.UserId, "Fork", success: false,
            $"Peer {peer.UserId} holds a different {head.StreamType} operation {head.HeadSequenceNumber} than we do: the author signed two versions.");
    }
}
