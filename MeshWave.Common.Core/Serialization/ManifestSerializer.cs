using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MeshWave.Common.Core.Models;
using NLog;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization.Protobuf;

namespace MeshWave.Common.Core.Serialization;

/// <summary>
/// Provides Protobuf serialization/deserialization for domain models and protocol messages.
/// </summary>
public static class ManifestSerializer
{
    public static byte[] SerializeRequest(ManifestRequest request)
    {
        var proto = MapToProto(request);
        return proto.ToByteArray();
    }

    public static ManifestRequest DeserializeRequest(byte[] data)
    {
        if (data.Length > SecurityLimits.MaxMessageBytes)
            throw new System.IO.InvalidDataException($"Rejected message: length {data.Length} exceeds limit.");
        var proto = ProtoManifestRequest.Parser.ParseFrom(data);
        return MapFromProto(proto);
    }

    public static byte[] SerializeResponse(ManifestResponse response)
    {
        var proto = MapToProto(response);
        return proto.ToByteArray();
    }

    public static ManifestResponse DeserializeResponse(byte[] data)
    {
        if (data.Length > SecurityLimits.MaxMessageBytes)
            throw new System.IO.InvalidDataException($"Rejected message: length {data.Length} exceeds limit.");
        var proto = ProtoManifestResponse.Parser.ParseFrom(data);
        return MapFromProto(proto);
    }

    // --- Mapping Helpers ---

    private static ProtoManifestRequest MapToProto(ManifestRequest request)
    {
        var proto = new ProtoManifestRequest
        {
            Type = (ProtoManifestRequestType)request.Type,
            StreamType = (ProtoManifestStreamType)request.StreamType,
            StartSequenceNumber = request.StartSequenceNumber
        };

        if (request.Manifest != null) proto.Manifest = MapToProto(request.Manifest);
        if (request.Rendezvous != null) proto.Rendezvous = MapToProto(request.Rendezvous);
        if (request.ContentHash != null) proto.ContentHash = request.ContentHash;
        if (request.AnnouncingPeer != null) proto.AnnouncingPeer = MapToProto(request.AnnouncingPeer);
        if (request.EndSequenceNumber.HasValue) proto.EndSequenceNumber = request.EndSequenceNumber.Value;
        if (request.TargetUserId != null) proto.TargetUserId = request.TargetUserId;

        return proto;
    }

    private static ManifestRequest MapFromProto(ProtoManifestRequest proto)
    {
        return new ManifestRequest
        {
            Type = (ManifestRequestType)proto.Type,
            StreamType = (ManifestStreamType)proto.StreamType,
            Manifest = proto.Manifest != null ? MapFromProto(proto.Manifest) : null,
            Rendezvous = proto.Rendezvous != null ? MapFromProto(proto.Rendezvous) : null,
            ContentHash = proto.HasContentHash ? proto.ContentHash : null,
            AnnouncingPeer = proto.AnnouncingPeer != null ? MapFromProto(proto.AnnouncingPeer) : null,
            StartSequenceNumber = proto.StartSequenceNumber,
            EndSequenceNumber = proto.HasEndSequenceNumber ? proto.EndSequenceNumber : null,
            TargetUserId = proto.HasTargetUserId ? proto.TargetUserId : null
        };
    }

    private static ProtoManifestResponse MapToProto(ManifestResponse response)
    {
        var proto = new ProtoManifestResponse
        {
            Acknowledged = response.Acknowledged,
            ContentLength = response.ContentLength
        };

        if (response.Manifest != null) proto.Manifest = MapToProto(response.Manifest);
        if (response.Peers != null) proto.Peers.AddRange(response.Peers.Select(MapToProto));
        if (response.Rendezvous != null) proto.Rendezvous = MapToProto(response.Rendezvous);
        if (response.ContentBytes != null) proto.ContentBytes = ByteString.CopyFrom(response.ContentBytes);

        return proto;
    }

    private static ManifestResponse MapFromProto(ProtoManifestResponse proto)
    {
        return new ManifestResponse
        {
            Manifest = proto.Manifest != null ? MapFromProto(proto.Manifest) : null,
            Acknowledged = proto.Acknowledged,
            Peers = proto.Peers.Select(MapFromProto).ToList(),
            Rendezvous = proto.Rendezvous != null ? MapFromProto(proto.Rendezvous) : null,
            ContentBytes = proto.HasContentBytes ? proto.ContentBytes.ToByteArray() : null,
            ContentLength = proto.ContentLength
        };
    }

    public static ProtoManifest MapToProto(Manifest manifest)
    {
        var proto = new ProtoManifest
        {
            UserId = manifest.UserId,
            StreamType = (ProtoManifestStreamType)manifest.StreamType,
            Version = manifest.Version,
            LastUpdated = Timestamp.FromDateTime(manifest.LastUpdated.ToUniversalTime())
        };

        if (manifest.Snapshot != null) proto.Snapshot = MapToProto(manifest.Snapshot);
        if (manifest.Operations != null) proto.Operations.AddRange(manifest.Operations.Select(MapToProto));

        return proto;
    }

    public static Manifest MapFromProto(ProtoManifest proto)
    {
        var manifest = new Manifest
        {
            UserId = proto.UserId,
            StreamType = (ManifestStreamType)proto.StreamType,
            Snapshot = proto.Snapshot != null ? MapFromProto(proto.Snapshot) : null,
            Version = proto.Version,
            LastUpdated = proto.LastUpdated.ToDateTime(),
            Operations = new List<ManifestOperation>()
        };

        var logger = LogManager.GetCurrentClassLogger();

        foreach (var protoOp in proto.Operations)
        {
            if (manifest.Operations.Count >= SecurityLimits.MaxManifestOperations)
            {
                logger.Warn($"Dropped operations from {proto.UserId}: count exceeds limit {SecurityLimits.MaxManifestOperations}");
                break;
            }

            var op = MapFromProto(protoOp);

            if (op.OperationId.Length > SecurityLimits.MaxOperationIdLength)
            {
                logger.Warn($"Rejected operation from {proto.UserId}: OperationId length {op.OperationId.Length} exceeds limit {SecurityLimits.MaxOperationIdLength}");
                continue;
            }
            if (op.TargetId.Length > SecurityLimits.MaxTargetIdLength)
            {
                logger.Warn($"Rejected operation from {proto.UserId}: TargetId length {op.TargetId.Length} exceeds limit {SecurityLimits.MaxTargetIdLength}");
                continue;
            }
            if (op.TargetType.Length > SecurityLimits.MaxTargetTypeLength)
            {
                logger.Warn($"Rejected operation from {proto.UserId}: TargetType length {op.TargetType.Length} exceeds limit {SecurityLimits.MaxTargetTypeLength}");
                continue;
            }
            if (op.ContentHash?.Length > SecurityLimits.MaxContentHashLength)
            {
                logger.Warn($"Rejected operation from {proto.UserId}: ContentHash length {op.ContentHash.Length} exceeds limit {SecurityLimits.MaxContentHashLength}");
                continue;
            }
            if (op.Metadata.Count > SecurityLimits.MaxMetadataEntries)
            {
                logger.Warn($"Rejected operation from {proto.UserId}: Metadata count {op.Metadata.Count} exceeds limit {SecurityLimits.MaxMetadataEntries}");
                continue;
            }

            var metadataValid = true;
            foreach (var kv in op.Metadata)
            {
                if (kv.Key.Length > SecurityLimits.MaxMetadataKeyLength)
                {
                    logger.Warn($"Rejected operation from {proto.UserId}: Metadata Key length {kv.Key.Length} exceeds limit {SecurityLimits.MaxMetadataKeyLength}");
                    metadataValid = false;
                    break;
                }
                if (kv.Value.Length > SecurityLimits.MaxMetadataValueLength)
                {
                    logger.Warn($"Rejected operation from {proto.UserId}: Metadata Value length {kv.Value.Length} exceeds limit {SecurityLimits.MaxMetadataValueLength}");
                    metadataValid = false;
                    break;
                }
            }
            if (!metadataValid) continue;

            manifest.Operations.Add(op);
        }

        return manifest;
    }

    private static ProtoManifestOperation MapToProto(ManifestOperation op)
    {
        var proto = new ProtoManifestOperation
        {
            OperationId = op.OperationId,
            OperationType = (ProtoManifestOperationType)op.OperationType,
            TargetId = op.TargetId,
            TargetType = op.TargetType,
            SequenceNumber = op.SequenceNumber,
            Signature = op.Signature,
            Timestamp = Timestamp.FromDateTime(op.Timestamp.ToUniversalTime())
        };

        if (op.ContentHash != null) proto.ContentHash = op.ContentHash;
        if (op.Metadata != null)
            foreach (var kv in op.Metadata) proto.Metadata.Add(kv.Key, kv.Value);

        return proto;
    }

    private static ManifestOperation MapFromProto(ProtoManifestOperation proto)
    {
        var parsedType = System.Enum.IsDefined(typeof(ManifestOperationType), (ManifestOperationType)proto.OperationType)
            ? (ManifestOperationType)proto.OperationType
            : ManifestOperationType.Unknown;

        var op = new ManifestOperation
        {
            OperationId = proto.OperationId,
            OperationType = parsedType,
            TargetId = proto.TargetId,
            TargetType = proto.TargetType,
            ContentHash = proto.HasContentHash ? proto.ContentHash : null,
            SequenceNumber = proto.SequenceNumber,
            Signature = proto.Signature,
            Timestamp = proto.Timestamp.ToDateTime(),
            Metadata = new Dictionary<string, string>(proto.Metadata)
        };
        return op;
    }

    private static ProtoManifestSnapshot MapToProto(ManifestSnapshot snapshot)
    {
        var proto = new ProtoManifestSnapshot
        {
            LastSequenceNumber = snapshot.LastSequenceNumber,
            Timestamp = Timestamp.FromDateTime(snapshot.Timestamp.ToUniversalTime()),
            Signature = snapshot.Signature
        };

        if (snapshot.LibraryStateDigest != null) proto.LibraryStateDigest = snapshot.LibraryStateDigest;
        if (snapshot.PlayCounts != null)
            foreach (var kv in snapshot.PlayCounts) proto.PlayCounts.Add(kv.Key, kv.Value);
        if (snapshot.FollowedUserIds != null) proto.FollowedUserIds.AddRange(snapshot.FollowedUserIds);
        if (snapshot.LikedTrackIds != null) proto.LikedTrackIds.AddRange(snapshot.LikedTrackIds);
        if (snapshot.FriendUserIds != null) proto.FriendUserIds.AddRange(snapshot.FriendUserIds);
        if (snapshot.GroupIds != null) proto.GroupIds.AddRange(snapshot.GroupIds);
        if (snapshot.EntityStates != null) proto.EntityStates.AddRange(snapshot.EntityStates.Select(MapToProto));
        if (snapshot.PersistentOperations != null) proto.PersistentOperations.AddRange(snapshot.PersistentOperations.Select(MapToProto));

        return proto;
    }

    private static ManifestSnapshot MapFromProto(ProtoManifestSnapshot proto)
    {
        return new ManifestSnapshot
        {
            LastSequenceNumber = proto.LastSequenceNumber,
            Timestamp = proto.Timestamp.ToDateTime(),
            Signature = proto.Signature,
            LibraryStateDigest = proto.HasLibraryStateDigest ? proto.LibraryStateDigest : null,
            PlayCounts = new Dictionary<string, int>(proto.PlayCounts),
            FollowedUserIds = proto.FollowedUserIds.ToList(),
            LikedTrackIds = proto.LikedTrackIds.ToList(),
            FriendUserIds = proto.FriendUserIds.ToList(),
            GroupIds = proto.GroupIds.ToList(),
            EntityStates = proto.EntityStates.Select(MapFromProto).ToList(),
            PersistentOperations = proto.PersistentOperations.Select(MapFromProto).ToList()
        };
    }

    private static ProtoSnapshotStateEntry MapToProto(SnapshotStateEntry entry)
    {
        var proto = new ProtoSnapshotStateEntry
        {
            TargetId = entry.TargetId,
            TargetType = entry.TargetType
        };
        if (entry.ContentHash != null) proto.ContentHash = entry.ContentHash;
        if (entry.Metadata != null)
            foreach (var kv in entry.Metadata) proto.Metadata.Add(kv.Key, kv.Value);
        return proto;
    }

    private static SnapshotStateEntry MapFromProto(ProtoSnapshotStateEntry proto)
    {
        return new SnapshotStateEntry
        {
            TargetId = proto.TargetId,
            TargetType = proto.TargetType,
            ContentHash = proto.HasContentHash ? proto.ContentHash : null,
            Metadata = new Dictionary<string, string>(proto.Metadata)
        };
    }

    private static ProtoPeerInfo MapToProto(PeerInfo peer)
    {
        var proto = new ProtoPeerInfo
        {
            UserId = peer.UserId,
            DisplayName = peer.DisplayName,
            Address = peer.Address,
            Port = peer.Port,
            PublicKeyPem = peer.PublicKeyPem,
            LastSeen = Timestamp.FromDateTime(peer.LastSeen.ToUniversalTime())
        };
        if (peer.Capabilities != null) proto.Capabilities.AddRange(peer.Capabilities);
        return proto;
    }

    private static PeerInfo MapFromProto(ProtoPeerInfo proto)
    {
        return new PeerInfo
        {
            UserId = proto.UserId,
            DisplayName = proto.DisplayName,
            Address = proto.Address,
            Port = proto.Port,
            PublicKeyPem = proto.PublicKeyPem,
            LastSeen = proto.LastSeen.ToDateTime(),
            Capabilities = proto.Capabilities.ToList()
        };
    }

    private static ProtoRendezvousRequest MapToProto(RendezvousRequest request)
    {
        return new ProtoRendezvousRequest
        {
            InitiatorUserId = request.InitiatorUserId,
            TargetUserId = request.TargetUserId,
            InitiatorPort = request.InitiatorPort,
            RequestedProbeWindowMs = request.RequestedProbeWindowMs
        };
    }

    private static RendezvousRequest MapFromProto(ProtoRendezvousRequest proto)
    {
        return new RendezvousRequest
        {
            InitiatorUserId = proto.InitiatorUserId,
            TargetUserId = proto.TargetUserId,
            InitiatorPort = proto.InitiatorPort,
            RequestedProbeWindowMs = proto.RequestedProbeWindowMs
        };
    }

    private static ProtoRendezvousResponse MapToProto(RendezvousResponse response)
    {
        return new ProtoRendezvousResponse
        {
            Success = response.Success,
            SessionId = response.SessionId,
            ExpiresAtUtc = Timestamp.FromDateTime(response.ExpiresAtUtc.ToUniversalTime()),
            ProbeStartUtc = Timestamp.FromDateTime(response.ProbeStartUtc.ToUniversalTime()),
            ProbeWindowMs = response.ProbeWindowMs,
            Message = response.Message
        };
    }

    private static RendezvousResponse MapFromProto(ProtoRendezvousResponse proto)
    {
        return new RendezvousResponse
        {
            Success = proto.Success,
            SessionId = proto.SessionId,
            ExpiresAtUtc = proto.ExpiresAtUtc.ToDateTime(),
            ProbeStartUtc = proto.ProbeStartUtc.ToDateTime(),
            ProbeWindowMs = proto.ProbeWindowMs,
            Message = proto.Message
        };
    }
}
