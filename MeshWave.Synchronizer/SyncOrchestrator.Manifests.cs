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

    /// <summary>
    /// Loads the persisted local manifest for a stream, or null. Reads the append-only log; a manifest saved in the
    /// earlier whole-file JSON format is still read (it is re-signed as a hash-linked log when P2P starts).
    /// </summary>
    public Manifest? LoadLocalManifest(string userId, ManifestStreamType streamType)
    {
        var path = BuildLocalManifestPath(userId, streamType);
        try
        {
            var manifest = ManifestLog.Read(path);
            if (manifest != null) return manifest;

            var legacyPath = Path.ChangeExtension(path, ".json");
            return File.Exists(legacyPath) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(legacyPath)) : null;
        }
        catch { return null; }
    }

    /// <summary>Last persisted state of each local stream, so that saving appends only the new operations.</summary>
    private readonly Dictionary<ManifestStreamType, (ManifestSnapshot? Snapshot, int Head)> _persistedLocal = [];

    private void SaveLocalManifest(Manifest manifest)
    {
        var path = BuildLocalManifestPath(manifest.UserId, manifest.StreamType);
        try
        {
            lock (manifest)
            {
                var head = ManifestManager.GetHeadSequenceNumber(manifest);
                lock (_persistedLocal)
                {
                    if (_persistedLocal.TryGetValue(manifest.StreamType, out var persisted) && ReferenceEquals(persisted.Snapshot, manifest.Snapshot) && File.Exists(path))
                        ManifestLog.Append(path, manifest.Operations.Where(o => o.SequenceNumber > persisted.Head).OrderBy(o => o.SequenceNumber));
                    else
                        ManifestLog.Rewrite(path, manifest);
                    _persistedLocal[manifest.StreamType] = (manifest.Snapshot, head);
                }
            }

            var legacyPath = Path.ChangeExtension(path, ".json");
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
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
        return Path.Combine(dir, $"{safeName}.{suffix}{ManifestLog.Extension}");
    }

    /// <summary>
    /// Called after a local operation was appended: compacts the stream if needed, appends the operation to disk and
    /// schedules a debounced delta push to neighbours.
    /// </summary>
    private void PersistAndFanoutLocalManifest(ManifestStreamType streamType)
    {
        var manifest = GetLocalManifest(streamType);
        if (manifest == null) return;

        Manifest copy;
        lock (manifest)
        {
            if (manifest.Operations.Count >= 500 && Identity != null)
            {
                _logger.Info("Compacting local {0} manifest ({1} operations)", streamType, manifest.Operations.Count);
                _manifestManager.Compact(manifest, Identity.PrivateKeyPem, threshold: 500, keepRecent: 100);
            }

            SaveLocalManifest(manifest);
            _logger.Debug("Local {0} manifest updated (head: {1}). Scheduling delta push.", streamType, ManifestManager.GetHeadSequenceNumber(manifest));

            // Copy for the catalogue, which reads it asynchronously while further operations may be appended.
            copy = new Manifest
            {
                UserId = manifest.UserId,
                StreamType = manifest.StreamType,
                Snapshot = manifest.Snapshot,
                Operations = manifest.Operations.ToList(),
                Version = manifest.Version,
                LastUpdated = manifest.LastUpdated
            };
        }

        _ = CatalogueService.IngestAsync(copy);
        ScheduleFanout(streamType);
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
