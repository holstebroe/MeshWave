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
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Storage;
using MeshWave.Common.Core.Validation;
using NLog;

namespace MeshWave.Synchronizer;

public partial class SyncOrchestrator
{
    public void ClearPeerManifestCache()
    {
        _peerStore.ClearAll();
    }

    public void SaveLocalManifests()
    {
        if (Identity == null) return;
        foreach (var kvp in _localManifests) SaveLocalManifest(kvp.Value);
    }

    public void SaveLocalManifest()
    {
        SaveLocalManifests();
    }

    public Manifest? LoadLocalManifest(string userId, ManifestStreamType streamType)
    {
        var path = BuildLocalManifestPath(userId, streamType);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Manifest>(json);
        }
        catch { return null; }
    }

    private void SaveLocalManifest(Manifest manifest)
    {
        var path = BuildLocalManifestPath(manifest.UserId, manifest.StreamType);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            lock (manifest)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(manifest));
            }
        }
        catch { /* best-effort disk write */ }
    }

    private string BuildLocalManifestPath(string userId, ManifestStreamType streamType)
    {
        var safeName = string.Concat(userId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var suffix = streamType.ToString().ToLowerInvariant();
        var baseFolder = UserRepository?.BaseDataFolder ?? _environment.GetAppDataRoot();
        var dir = Path.Combine(baseFolder, "LocalManifests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{safeName}.{suffix}.json");
    }

    private void PersistAndFanoutLocalManifest(ManifestStreamType streamType)
    {
        var manifest = GetLocalManifest(streamType);
        if (manifest == null) return;

        Manifest manifestToShare;
        lock (manifest)
        {
            if (manifest.Operations.Count >= 500 && Identity != null)
            {
                _logger.Info("Compacting local {0} manifest ({1} operations)", streamType, manifest.Operations.Count);
                _manifestManager.Compact(manifest, Identity.PrivateKeyPem, threshold: 500, keepRecent: 100);
            }

            SaveLocalManifest(manifest);

            _logger.Info("Local {0} manifest updated (ops: {1}). Initiating fan-out to peers.", streamType, manifest.Operations.Count);

            // Clone for sharing to avoid race conditions with further modifications/compactions
            manifestToShare = new Manifest
            {
                UserId = manifest.UserId,
                StreamType = manifest.StreamType,
                Snapshot = manifest.Snapshot,
                Operations = manifest.Operations.ToList(),
                Version = manifest.Version,
                LastUpdated = manifest.LastUpdated
            };
            }

        _ = CatalogueService.IngestAsync(manifestToShare);

        _ = Task.Run(async () =>
            {
            var meshPeers = _router.GetPeers().Where(p => !p.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var peer in meshPeers)
                try
                {
                    _logger.Debug("Pushing local {0} manifest to peer {1} ({2}:{3})", streamType, peer.UserId, peer.Address, peer.Port);
                    await _client.PushManifestAsync(peer.Address, peer.Port, manifestToShare, BuildAnnouncingPeerInfo(streamType));
                    RecordPeerMessage(peer.UserId, "PushManifest", success: true,
                        $"Pushed local {streamType} manifest ({manifestToShare.Operations.Count} op) to {peer.Address}:{peer.Port}.");
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to push {0} manifest to {1}: {2}", streamType, peer.UserId, ex.Message);
                    RecordPeerMessage(peer.UserId, "PushManifest", success: false,
                        $"Push failed for {streamType} to {peer.Address}:{peer.Port}: {ex.Message}");
                    // best-effort push; periodic sync/merge will reconcile later
                }

            if (!_actAsListener)
                foreach (var bootstrap in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
                    if (TryParseEndpoint(bootstrap, out var host, out var port))
                        try
                {
                            _logger.Debug("Relaying local {0} manifest via bootstrap {1}:{2}", streamType, host, port);
                            await _client.RelayManifestPushAsync(host, port, manifestToShare, BuildAnnouncingPeerInfo(streamType));
                            RecordPeerMessage($"bootstrap:{host}:{port}", "RelayManifestPush", success: true,
                                $"Pushed local {streamType} manifest to bootstrap for relaying.");
                }
                        catch (Exception ex)
                {
                            _logger.Warn("Failed to relay {0} manifest via bootstrap {1}:{2}: {3}", streamType, host, port, ex.Message);
                            RecordPeerMessage($"bootstrap:{host}:{port}", "RelayManifestPush", success: false,
                                $"Relay push failed for {streamType}: {ex.Message}");
                }
        });
            }

    private void TryMerge(Manifest remote, string publicKeyPem)
            {
        if (remote.UserId == Identity?.UserId) return;

        _logger.Debug("Attempting merge of manifest from peer {0} ({1} ops, stream={2})", remote.UserId, remote.Operations.Count, remote.StreamType);
        var existingManifest = _peerStore.Get(remote.UserId, remote.StreamType);
        var existingCount = existingManifest?.Operations.Count ?? 0;

        var added = _peerStore.MergeAndSave(remote, publicKeyPem, _manifestManager);
        if (added > 0)
                {
            _logger.Info("Merged manifest from peer {0}: added {1} new operations.", remote.UserId, added);
            var profileOp = remote.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();

            if (profileOp != null)
                    {
                _logger.Debug("Updating profile for {0} from merged manifest", remote.UserId);
                UserRepository?.UpdateProfile(remote.UserId, profileOp.Metadata);
                var iconHash = profileOp.ContentHash;
                if (!string.IsNullOrWhiteSpace(iconHash))
                    _ = Task.Run(async () =>
                        {
                        try
                            {
                            var bytes = await RequestContentAsync(remote.UserId, iconHash);
                            if (bytes != null) UserRepository?.SaveUserIcon(remote.UserId, bytes);
                            }
                        catch { }
                    });
                        }

            _ = CatalogueService.IngestAsync(remote);

            // Also trigger icon/content downloads for catalogue entries
            foreach (var op in remote.Operations)
                if (op.OperationType == ManifestOperationType.Create || op.OperationType == ManifestOperationType.Update)
                        {
                    var iconHash = op.Metadata.GetValueOrDefault("iconHash");
                    if (string.IsNullOrWhiteSpace(iconHash) && op.TargetType == "User")
                        iconHash = op.ContentHash;

                    if (!string.IsNullOrWhiteSpace(iconHash))
                        _ = Task.Run(async () =>
                            {
                            try
                                {
                                var bytes = await RequestContentAsync(remote.UserId, iconHash);
                                if (bytes != null && UserRepository != null) UserRepository.SaveUserIcon(op.TargetId, bytes);
                                }
                            catch { }
                        });
                    else if (!string.IsNullOrWhiteSpace(op.ContentHash) && (op.Metadata.ContainsKey("isIcon") && op.Metadata["isIcon"] == "True"))
                        _ = Task.Run(async () =>
                                {
                            try
                                    {
                                var bytes = await RequestContentAsync(remote.UserId, op.ContentHash!);
                                if (bytes != null && UserRepository != null) UserRepository.SaveUserIcon(op.TargetId, bytes);
                                    }
                            catch { }
                        });
                                }

            foreach (var op in remote.Operations)
                                {
                if (op.SequenceNumber >= existingCount && op.OperationType == ManifestOperationType.PostMessage)
                                    {
                    GroupMessageReceived?.Invoke(this, new GroupMessageEventArgs(
                        remote.UserId,
                        op.Metadata?.GetValueOrDefault("channelId") ?? string.Empty,
                        op.TargetId,
                        op.Metadata?.GetValueOrDefault("content") ?? string.Empty,
                        op.Metadata?.GetValueOrDefault("parentPostId")
                    ));
                                    }
                else if (op.SequenceNumber >= existingCount && (op.OperationType == ManifestOperationType.CreateChannel || op.OperationType == ManifestOperationType.FoundGroup || op.OperationType == ManifestOperationType.ModerateGroup || op.OperationType == ManifestOperationType.GroupJoin || op.OperationType == ManifestOperationType.GroupLeave))
                                    {
                    GroupStateChanged?.Invoke(this, new GroupStateChangedEventArgs(remote.UserId, op.OperationType, op.TargetId, op.Metadata ?? new Dictionary<string, string>()));
                                    }
                                }

            ManifestMerged?.Invoke(this, new ManifestMergedEventArgs(remote.UserId, added));
                            }
        else
                            {
            _logger.Trace("Merge of manifest from peer {0} resulted in 0 new operations.", remote.UserId);
                            }
                        }

    private static int CountPublishedItems(Manifest? manifest, string targetType)
                        {
        if (manifest == null)
            return 0;

        return manifest.Operations
            .Where(op => string.Equals(op.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                      && (op.OperationType == ManifestOperationType.Create
                       || op.OperationType == ManifestOperationType.Update
                       || op.OperationType == ManifestOperationType.Delete))
            .GroupBy(op => op.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(op => op.SequenceNumber).First())
            .Count(op => op.OperationType != ManifestOperationType.Delete);
                        }

    private static string ResolveDisplayName(Manifest? manifest, PeerInfo? peer)
                        {
        var profileOp = manifest?.Operations
            .Where(op => op.OperationType == ManifestOperationType.Profile)
            .OrderByDescending(op => op.SequenceNumber)
            .FirstOrDefault();

        var profileName = profileOp?.Metadata.GetValueOrDefault("displayName");
        if (!string.IsNullOrWhiteSpace(profileName))
            return profileName;

        if (!string.IsNullOrWhiteSpace(peer?.DisplayName) && !peer.DisplayName.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase))
            return peer.DisplayName;

        if (!string.IsNullOrWhiteSpace(manifest?.UserId))
            return manifest.UserId;

        if (!string.IsNullOrWhiteSpace(peer?.UserId))
            return peer.UserId;

        if (!string.IsNullOrWhiteSpace(peer?.Address))
            return $"{peer.Address}:{peer.Port}";

        return "Unknown Peer";
                        }

                    }
