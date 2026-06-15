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
    public void AnnounceTrack(string trackId, string contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Create));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta.TryAdd("releasedAt", DateTime.UtcNow.ToString("O"));

        var title = meta.GetValueOrDefault("title") ?? trackId;
        _logger.Info("Announcing track release: '{0}' (ID: {1}, Hash: {2})", title, trackId, contentHash);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Create,
            trackId,
            "Track",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void UpdateTrack(string trackId, string contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Update));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];

        var title = meta.GetValueOrDefault("title") ?? trackId;
        _logger.Info("Announcing track update: '{0}' (ID: {1}, Hash: {2})", title, trackId, contentHash);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Update,
            trackId,
            "Track",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void AnnounceAlbum(string albumId, string? contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Create));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta.TryAdd("releasedAt", DateTime.UtcNow.ToString("O"));

        var name = meta.GetValueOrDefault("name") ?? albumId;
        _logger.Info("Announcing album release: '{0}' (ID: {1})", name, albumId);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Create,
            albumId,
            "Album",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void UpdateAlbum(string albumId, string? contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Update));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];

        var name = meta.GetValueOrDefault("name") ?? albumId;
        _logger.Info("Announcing album update: '{0}' (ID: {1})", name, albumId);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Update,
            albumId,
            "Album",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public bool RecordPlay(string trackId, string title, string artist, string? contentHash = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Play));
        if (manifest == null || Identity == null) return false;
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        if (!_playedThisSession.Add(trackId)) return false;   // already counted this session

        _logger.Info("Recording play for track '{0}' (ID: {1})", title, trackId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Play,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: contentHash,
            new Dictionary<string, string>
        {
                ["title"] = SecurityLimits.Truncate(title, SecurityLimits.MaxTrackTitleLength),
                ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength)
            },
            Identity.PrivateKeyPem);

        return true;
        }

    }
