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
    public void RecordFoundGroup(string groupId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.FoundGroup));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording group found for group {0}", groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.FoundGroup,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void RecordModerateGroup(string groupId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.ModerateGroup));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording group moderate for group {0}", groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.ModerateGroup,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void RecordCreateChannel(string channelId, string groupId, string name)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.CreateChannel));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording create channel {0} in group {1}", channelId, groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.CreateChannel,
            SecurityLimits.Truncate(channelId, SecurityLimits.MaxTargetIdLength),
            "GroupChannel",
            contentHash: null,
            metadata: new Dictionary<string, string>
        {
                ["groupId"] = SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
                ["name"] = SecurityLimits.Truncate(name, 100)
            },
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
        }

    public void RecordPostMessage(string postId, string channelId, string content, string? parentPostId = null)
        {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.PostMessage));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(postId) || string.IsNullOrWhiteSpace(channelId)) return;

        _logger.Info("Recording post message {0} in channel {1}", postId, channelId);

        var meta = new Dictionary<string, string>
            {
            ["channelId"] = SecurityLimits.Truncate(channelId, SecurityLimits.MaxTargetIdLength),
            ["content"] = SecurityLimits.Truncate(content, SecurityLimits.MaxCommentTextLength)
        };

        if (!string.IsNullOrWhiteSpace(parentPostId))
                {
            meta["parentPostId"] = SecurityLimits.Truncate(parentPostId, SecurityLimits.MaxTargetIdLength);
                }

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.PostMessage,
            SecurityLimits.Truncate(postId, SecurityLimits.MaxTargetIdLength),
            "GroupChannel",
            contentHash: null,
            metadata: meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
            }

    public void RecordGroupJoin(string groupId)
            {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.GroupJoin));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording group join for group {0}", groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.GroupJoin,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
            }

    public void RecordGroupLeave(string groupId)
            {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.GroupLeave));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording group leave for group {0}", groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.GroupLeave,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
            }

        }
