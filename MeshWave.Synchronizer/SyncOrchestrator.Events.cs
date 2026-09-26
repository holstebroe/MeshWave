using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Storage;
using MeshWave.Common.Core.Validation;
using NLog;

namespace MeshWave.Synchronizer;

public partial class SyncOrchestrator
{
    private void OnPeerAdded(object? sender, PeerInfo peer)
    {
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
        if (IsBootstrapEntry(peer))
            return;

        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            // Open a persistent session first (TCP, or hole-punched UDP for peers without an open port),
            // so that the fetch and the pushes below can use it.
            try { await TryOpenSessionAsync(peer, ct); } catch { }
            await TryFetchAndMergeAsync(peer, ct);

            if (!HasRoute(peer))
                return;

            foreach (var streamType in Enum.GetValues<ManifestStreamType>())
            {
                var manifest = GetLocalManifest(streamType);
                if (manifest == null)
                {
                    _logger.Debug($"OnPeerAdded: No {streamType} manifest available for {peer.UserId}");
                    continue;
                }
                _logger.Debug($"OnPeerAdded: Pushing {streamType} manifest ({manifest.Operations.Count} ops) to {peer.UserId}");

                Manifest manifestToPush;
                lock (manifest)
                {
                    manifestToPush = new Manifest
                    {
                        UserId = manifest.UserId,
                        StreamType = manifest.StreamType,
                        Snapshot = manifest.Snapshot,
                        Operations = manifest.Operations.ToList(),
                        Version = manifest.Version,
                        LastUpdated = manifest.LastUpdated
                    };
                }

                try
                {
                    var acknowledged = await _client.PushManifestAsync(peer, manifestToPush, BuildAnnouncingPeerInfo(manifestToPush.StreamType), ct);
                    if (acknowledged) _router.MarkContacted(peer.UserId);
                    RecordPeerMessage(peer.UserId, "PushManifest", success: acknowledged,
                        $"Pushed local {manifestToPush.StreamType} manifest ({manifestToPush.Operations.Count} op) to {DescribeRoute(peer)}.");
                }
                catch (Exception ex)
                {
                    RecordPeerMessage(peer.UserId, "PushManifest", success: false,
                        $"Push failed for {manifestToPush.StreamType} to {DescribeRoute(peer)}: {ex.Message}");
                }
            }
        });
    }

    private string DescribeRoute(PeerInfo peer)
    {
        var session = _sessions.GetSession(peer.UserId);
        return session != null ? $"{session.TransportKind} session {session.Id}" : $"{peer.Address}:{peer.Port}";
    }

    private void OnPeerAnnounced(object? sender, PeerAnnouncedEventArgs e)
            {
        if (!CryptoService.IsPublicKeyForUser(e.Peer.UserId, e.Peer.PublicKeyPem))
            {
            _logger.Warn("Ignored announcement for user {0} from {1}: public key does not match the UserId.", e.Peer.UserId, e.Peer.Address);
            return;
            }

        RecordPeerMessage(e.Peer.UserId, "Announce", success: true,
            $"Peer announced from {e.Peer.Address} (port {e.Peer.Port}{(e.Peer.Port > 0 ? string.Empty : ", outbound-only")}).");
        _router.LearnPeerDirect(e.Peer);
            }

    private void OnPeerRemoved(object? sender, string userId)
            {
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
            }

    private void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
            {
        // Ignore pushes from ourselves
        if (e.Manifest.UserId == Identity?.UserId)
                {
            _logger.Debug("Ignored manifest push from self ({0})", e.Manifest.UserId);
            return;
                }

        Interlocked.Increment(ref _inboundManifestPushCount);
        RecordPeerMessage(e.Manifest.UserId, "PushManifest", success: true,
            $"Received manifest with {e.Manifest.Operations.Count} operation(s) from {e.PeerAddress}.");

        var peer = _router.GetPeers().FirstOrDefault(p => p.UserId == e.Manifest.UserId);

        // UserIds are derived from public keys, so only a key that hashes to the manifest's UserId may be used
        // to verify it. Without this check anyone could push a manifest for another user signed with their own key.
        var publicKeyPem = new[]
            {
                peer?.PublicKeyPem,
                e.AnnouncingPeer?.PublicKeyPem,
                e.Manifest.Operations
                    .Where(op => op.OperationType == ManifestOperationType.Profile)
                    .OrderByDescending(op => op.SequenceNumber)
                    .Select(op => op.Metadata.GetValueOrDefault("publicKeyPem"))
                    .FirstOrDefault(pk => !string.IsNullOrWhiteSpace(pk))
            }
            .FirstOrDefault(pk => CryptoService.IsPublicKeyForUser(e.Manifest.UserId, pk));

        if (string.IsNullOrWhiteSpace(publicKeyPem))
            {
            _logger.Warn("Rejected manifest push for user {0} from {1}: no public key matching the UserId.", e.Manifest.UserId, e.PeerAddress);
            return;
            }

        if (peer == null)
                {
            var profile = e.Manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();

            var discovered = new PeerInfo
                    {
                UserId = e.Manifest.UserId,
                DisplayName = SecurityLimits.Truncate(
                    profile?.Metadata.GetValueOrDefault("displayName")
                    ?? e.AnnouncingPeer?.DisplayName
                    ?? e.Manifest.UserId,
                    SecurityLimits.MaxDisplayNameLength),
                Address = e.PeerAddress,
                // Port 0 means the sender is outbound-only; only fall back to the default port for senders that don't announce.
                Port = e.AnnouncingPeer != null ? Math.Max(0, e.AnnouncingPeer.Port) : ManifestExchangeServer.DefaultPort,
                LastSeen = DateTime.UtcNow,
                PublicKeyPem = publicKeyPem
            };

            // A push is not proof that its author is online (anyone can forward a signed manifest),
            // so the sender only introduces an unknown peer; it does not refresh a known one.
            _router.LearnPeers([discovered]);
                    }

        TryMerge(e.Manifest, publicKeyPem);
                }

    private async Task TryFetchAndMergeAsync(PeerInfo peer, CancellationToken ct)
    {
        if (!CryptoService.IsPublicKeyForUser(peer.UserId, peer.PublicKeyPem)) return;
        if (peer.UserId == Identity?.UserId) return;
        if (!HasRoute(peer)) return;

        foreach (ManifestStreamType streamType in Enum.GetValues(typeof(ManifestStreamType)))
            try
            {
                var existing = _peerStore.Get(peer.UserId, streamType);
                var startSeq = ManifestManager.GetHeadSequenceNumber(existing) + 1;

                var remoteManifest = await _client.FetchManifestAsync(peer, _peerStore, streamType, ct);
                _router.MarkContacted(peer.UserId);

                if (remoteManifest == null)
                {
                    RecordPeerMessage(peer.UserId, "FetchManifest", success: false,
                        $"Peer {DescribeRoute(peer)} returned no {streamType} manifest.");
                    continue;
                }

                Interlocked.Increment(ref _outboundManifestFetchCount);
                var details = $"Fetched {streamType} manifest with {remoteManifest.Operations.Count} operation(s) (delta sync from seq {startSeq}) via {DescribeRoute(peer)}.";
                _logger.Debug(details);
                RecordPeerMessage(peer.UserId, "FetchManifest", success: true, details);
                TryMerge(remoteManifest, peer.PublicKeyPem);
            }
            catch (Exception ex)
            {
                RecordPeerMessage(peer.UserId, "FetchManifest", success: false,
                    $"Fetch failed for {streamType}: {ex.Message}");
            }
    }
}
