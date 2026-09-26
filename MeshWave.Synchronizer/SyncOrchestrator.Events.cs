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
        _ = Task.Run(() => TryFetchAndMergeAsync(peer, _cts?.Token ?? CancellationToken.None));

        if (peer.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase))
            return;

        // Outbound-only peers have no listener; they fetch our manifests themselves.
        if (!PeerRouter.IsDialable(peer))
            return;

        _ = Task.Run(async () =>
        {
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
                    await _client.PushManifestAsync(peer.Address, peer.Port, manifestToPush, BuildAnnouncingPeerInfo(manifestToPush.StreamType));
                    RecordPeerMessage(peer.UserId, "PushManifest", success: true,
                        $"Pushed local {manifestToPush.StreamType} manifest ({manifestToPush.Operations.Count} op) to {peer.Address}:{peer.Port}.");
                    }
                catch (Exception ex)
                    {
                    RecordPeerMessage(peer.UserId, "PushManifest", success: false,
                        $"Push failed for {manifestToPush.StreamType} to {peer.Address}:{peer.Port}: {ex.Message}");
                    }
                }
        });
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
        _router.LearnPeers([e.Peer]);
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

            _router.LearnPeers([discovered]);
                    }

        TryMerge(e.Manifest, publicKeyPem);
                }

    private async Task TryFetchAndMergeAsync(PeerInfo peer, CancellationToken ct)
                {
        if (!CryptoService.IsPublicKeyForUser(peer.UserId, peer.PublicKeyPem)) return;
        if (peer.UserId == Identity?.UserId) return;

        foreach (ManifestStreamType streamType in Enum.GetValues(typeof(ManifestStreamType)))
            try
                    {
                var existing = _peerStore.Get(peer.UserId, streamType);
                var startSeq = ManifestManager.GetHeadSequenceNumber(existing) + 1;

                Manifest? remoteManifest = null;
                var fetchedFromPeer = false;

                try
                        {
                    if (peer.Port > 0)
                            {
                        remoteManifest = await _client.FetchManifestAsync(peer.Address, peer.Port, _peerStore, peer.UserId, streamType, ct);
                        fetchedFromPeer = remoteManifest != null;
                            }
                        }
                catch
                        {
                    /* fallback to relay if peer is unreachable */
                        }

                if (remoteManifest == null && peer.Capabilities.Contains("relay"))
                    foreach (var bootstrap in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
                        if (TryParseEndpoint(bootstrap, out var host, out var port))
                            try
                        {
                                remoteManifest = await _client.FetchManifestAsync(host, port, _peerStore, peer.UserId, streamType, ct);
                                if (remoteManifest != null)
                            {
                                    RecordPeerMessage(peer.UserId, "FetchManifestRelay", success: true,
                                        $"Fetched {streamType} manifest from bootstrap relay {host}:{port}.");
                                    break;
                            }
                        }
                            catch { }

                if (remoteManifest == null)
                        {
                    RecordPeerMessage(peer.UserId, "FetchManifest", success: false,
                        $"Peer {peer.Address}:{peer.Port} returned no {streamType} manifest and relay fallback failed.");
                    continue;
                        }

                Interlocked.Increment(ref _outboundManifestFetchCount);
                var details = $"Fetched {streamType} manifest with {remoteManifest.Operations.Count} operation(s) (delta sync from seq {startSeq}). FromPeer={fetchedFromPeer}";
                _logger.Debug(details);
                RecordPeerMessage(peer.UserId, "FetchManifest", success: true,
                    details);
                TryMerge(remoteManifest, peer.PublicKeyPem);
                    }
            catch (Exception ex)
                    {
                RecordPeerMessage(peer.UserId, "FetchManifest", success: false,
                    $"Fetch failed for {streamType}: {ex.Message}");
                    }
                }

            }
