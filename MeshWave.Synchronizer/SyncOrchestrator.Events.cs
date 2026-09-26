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
            // so that the heads exchange below can use it.
            try { await TryOpenSessionAsync(peer, ct); } catch { }
            // Pulls what the peer has that we lack and pushes our own streams it lacks.
            await SyncWithPeerAsync(peer, ct);
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
        ForgetPeerHeads(userId);
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
    {
        // Ignore pushes of our own streams
        if (e.Manifest.UserId == Identity?.UserId)
        {
            _logger.Debug("Ignored manifest push of our own stream ({0})", e.Manifest.UserId);
            return;
        }

        Interlocked.Increment(ref _inboundManifestPushCount);
        var senderUserId = e.AnnouncingPeer?.UserId;
        var fromAuthor = string.Equals(senderUserId, e.Manifest.UserId, StringComparison.OrdinalIgnoreCase);
        RecordPeerMessage(e.Manifest.UserId, "PushManifest", success: true,
            $"Received {e.Manifest.StreamType} delta with {e.Manifest.Operations.Count} operation(s) from {(fromAuthor ? "the author" : senderUserId ?? "unknown")} at {e.PeerAddress}.");

        // UserIds are derived from public keys, so only a key that hashes to the manifest's UserId may be used
        // to verify it. Without this check anyone could push a manifest for another user signed with their own key.
        var publicKeyPem = ResolveAuthorKey(e.Manifest.UserId, e.Manifest, e.AnnouncingPeer);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            // The push response reports that we hold nothing of this stream, so the sender follows up from the start, with the key.
            _logger.Debug("Deferred manifest push for user {0} from {1}: no public key matching the UserId yet.", e.Manifest.UserId, e.PeerAddress);
            return;
        }

        // Only a push from the author itself introduces the author as a peer; forwarded (gossiped) operations say nothing
        // about where their author can be reached. A push is also not proof that the author is online, so a known peer is not refreshed.
        if (fromAuthor && e.AnnouncingPeer != null && PeerRecords.IsValidlySigned(e.AnnouncingPeer)
            && _router.GetPeers().All(p => p.UserId != e.Manifest.UserId))
        {
            var profile = e.Manifest.Operations.LastOrDefault(op => op.OperationType == ManifestOperationType.Profile);
            _router.LearnPeers([new PeerInfo
            {
                UserId = e.Manifest.UserId,
                DisplayName = SecurityLimits.Truncate(
                    profile?.Metadata.GetValueOrDefault("displayName") ?? e.AnnouncingPeer.DisplayName ?? e.Manifest.UserId,
                    SecurityLimits.MaxDisplayNameLength),
                Address = e.PeerAddress,
                // Port 0 means the sender is outbound-only.
                Port = Math.Max(0, e.AnnouncingPeer.Port),
                LastSeen = DateTime.UtcNow,
                PublicKeyPem = publicKeyPem
            }]);
        }

        TryMerge(e.Manifest, publicKeyPem, senderUserId, forward: true);
    }
}
