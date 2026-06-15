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
    public void RecordFollow(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Follow));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording follow for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Follow,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void RecordUnfollow(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Unfollow));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording unfollow for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Unfollow,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void RecordFriendAdd(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.FriendAdd));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording friend add for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.FriendAdd,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void RecordFriendRemove(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.FriendRemove));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording friend remove for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.FriendRemove,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void BroadcastProfile(string displayName, bool isArtist, string bio, string? website, string? bannerImageHash)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Profile));
        if (manifest == null || Identity == null) return;

        _logger.Info("Broadcasting updated profile for {0} (isArtist: {1})", displayName, isArtist);
        var meta = new Dictionary<string, string>
        {
            ["displayName"] = SecurityLimits.Truncate(displayName, SecurityLimits.MaxArtistNameLength),
            ["isArtist"] = isArtist.ToString(),
            ["bio"] = SecurityLimits.Truncate(bio, 1000),
            ["website"] = SecurityLimits.Truncate(website, 256),
            ["publicKeyPem"] = Identity.PublicKeyPem
        };
        if (!string.IsNullOrWhiteSpace(bannerImageHash))
            meta["bannerImageHash"] = bannerImageHash;

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Profile,
            Identity.UserId,
            "User",
            contentHash: bannerImageHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
        }

    public string? RecordComment(string trackId, string commentText, string? replyToId = null, Dictionary<string, string>? metadata = null)
        {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Comment));
        if (manifest == null || Identity == null) return null;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(commentText)) return null;

        _logger.Info("Recording comment for track {0}: '{1}'", trackId, SecurityLimits.Truncate(commentText, 32));
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta["text"] = SecurityLimits.Truncate(commentText, SecurityLimits.MaxCommentTextLength);
        if (!string.IsNullOrWhiteSpace(replyToId))
            meta["replyToId"] = SecurityLimits.Truncate(replyToId, SecurityLimits.MaxOperationIdLength);

        var op = _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Comment,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: meta,
            Identity.PrivateKeyPem);

        PersistAndFanoutLocalManifest(manifest.StreamType);
        return op.OperationId;
        }

    public void RecordCommentDelete(string trackId, string commentOperationId)
        {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.CommentDelete));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(commentOperationId)) return;

        _logger.Info("Recording comment deletion for track {0}, op {1}", trackId, commentOperationId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.CommentDelete,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: new Dictionary<string, string>
            {
                ["commentOperationId"] = SecurityLimits.Truncate(commentOperationId, SecurityLimits.MaxOperationIdLength)
            },
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
            }

    public void RecordLike(string trackId)
            {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Like));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId)) return;

        _logger.Info("Recording like for track {0}", trackId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Like,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
            }

    public void RecordUnlike(string trackId)
            {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Unlike));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId)) return;

        _logger.Info("Recording unlike for track {0}", trackId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Unlike,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
            }

        }
