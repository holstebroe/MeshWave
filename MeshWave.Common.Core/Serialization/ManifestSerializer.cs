using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MeshWave.Common.Core.Models;
using NLog;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization.Protobuf;
using MeshWave.Common.Core.Validation;

namespace MeshWave.Common.Core.Serialization;

/// <summary>
/// Provides Protobuf serialization/deserialization for domain models and protocol messages.
/// </summary>
public static class ManifestSerializer
{
    public static IManifestOperationValidator Validator { get; set; } = new DefaultManifestOperationValidator();

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
        if (request.ContentHash != null) proto.ContentHash = request.ContentHash;
        if (request.AnnouncingPeer != null) proto.AnnouncingPeer = MapToProto(request.AnnouncingPeer);
        if (request.EndSequenceNumber.HasValue) proto.EndSequenceNumber = request.EndSequenceNumber.Value;
        if (request.ChunkOffset.HasValue) proto.ChunkOffset = request.ChunkOffset.Value;
        if (request.ChunkLength.HasValue) proto.ChunkLength = request.ChunkLength.Value;
        if (request.Hello != null) proto.Hello = MapToProto(request.Hello);
        if (request.Introduction != null) proto.Introduction = MapToProto(request.Introduction);

        return proto;
    }

    private static ManifestRequest MapFromProto(ProtoManifestRequest proto)
    {
        return new ManifestRequest
        {
            Type = (ManifestRequestType)proto.Type,
            StreamType = (ManifestStreamType)proto.StreamType,
            Manifest = proto.Manifest != null ? MapFromProto(proto.Manifest) : null,
            ContentHash = proto.HasContentHash ? proto.ContentHash : null,
            AnnouncingPeer = proto.AnnouncingPeer != null ? MapFromProto(proto.AnnouncingPeer) : null,
            StartSequenceNumber = proto.StartSequenceNumber,
            EndSequenceNumber = proto.HasEndSequenceNumber ? proto.EndSequenceNumber : null,
            ChunkOffset = proto.HasChunkOffset ? proto.ChunkOffset : null,
            ChunkLength = proto.HasChunkLength ? proto.ChunkLength : null,
            Hello = proto.Hello != null ? MapFromProto(proto.Hello) : null,
            Introduction = proto.Introduction != null ? MapFromProto(proto.Introduction) : null
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
        if (response.ContentBytes != null) proto.ContentBytes = ByteString.CopyFrom(response.ContentBytes);
        if (response.TotalContentLength.HasValue) proto.TotalContentLength = response.TotalContentLength.Value;
        if (response.Hello != null) proto.Hello = MapToProto(response.Hello);
        if (response.Introduction != null) proto.Introduction = MapToProto(response.Introduction);
        if (response.ObservedAddress != null) proto.ObservedAddress = response.ObservedAddress;
        if (response.DialBackSucceeded.HasValue) proto.DialBackSucceeded = response.DialBackSucceeded.Value;

        return proto;
    }

    private static ManifestResponse MapFromProto(ProtoManifestResponse proto)
    {
        return new ManifestResponse
        {
            Manifest = proto.Manifest != null ? MapFromProto(proto.Manifest) : null,
            Acknowledged = proto.Acknowledged,
            Peers = proto.Peers.Select(MapFromProto).ToList(),
            ContentBytes = proto.HasContentBytes ? proto.ContentBytes.ToByteArray() : null,
            ContentLength = proto.ContentLength,
            TotalContentLength = proto.HasTotalContentLength ? proto.TotalContentLength : null,
            Hello = proto.Hello != null ? MapFromProto(proto.Hello) : null,
            Introduction = proto.Introduction != null ? MapFromProto(proto.Introduction) : null,
            ObservedAddress = proto.HasObservedAddress ? proto.ObservedAddress : null,
            DialBackSucceeded = proto.HasDialBackSucceeded ? proto.DialBackSucceeded : null
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

            if (!Validator.IsValid(op, proto.UserId, out var rejectionReason))
            {
                logger.Warn($"Rejected operation from {proto.UserId}: {rejectionReason}");
                continue;
            }

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
        {
            foreach (var kv in op.Metadata)
            {
                if (kv.Key != "shaderScript") proto.Metadata.Add(kv.Key, kv.Value);
            }
            if (op.Metadata.TryGetValue("shaderScript", out var shaderScript))
                proto.ShaderScript = shaderScript;
        }

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

        if (proto.HasShaderScript && !string.IsNullOrWhiteSpace(proto.ShaderScript))
            op.Metadata["shaderScript"] = proto.ShaderScript;

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
        {
            foreach (var kv in entry.Metadata)
            {
                if (kv.Key != "shaderScript") proto.Metadata.Add(kv.Key, kv.Value);
            }
            if (entry.Metadata.TryGetValue("shaderScript", out var shaderScript))
                proto.ShaderScript = shaderScript;
        }
        return proto;
    }

    private static SnapshotStateEntry MapFromProto(ProtoSnapshotStateEntry proto)
    {
        var entry = new SnapshotStateEntry
        {
            TargetId = proto.TargetId,
            TargetType = proto.TargetType,
            ContentHash = proto.HasContentHash ? proto.ContentHash : null,
            Metadata = new Dictionary<string, string>(proto.Metadata)
        };

        if (proto.HasShaderScript && !string.IsNullOrWhiteSpace(proto.ShaderScript))
            entry.Metadata["shaderScript"] = proto.ShaderScript;

        return entry;
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
        if (peer.SignedAtUtc.HasValue) proto.SignedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(peer.SignedAtUtc.Value, DateTimeKind.Utc));
        proto.Signature = peer.Signature ?? string.Empty;
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
            Capabilities = proto.Capabilities.ToList(),
            SignedAtUtc = proto.SignedAt?.ToDateTime(),
            Signature = proto.Signature
        };
    }

    private static ProtoSessionHello MapToProto(SessionHello hello)
    {
        var proto = new ProtoSessionHello
        {
            Nonce = hello.Nonce,
            Proof = hello.Proof ?? string.Empty,
            UdpPort = hello.UdpPort,
            IsIntroducer = hello.IsIntroducer
        };
        if (hello.Peer != null) proto.Peer = MapToProto(hello.Peer);
        return proto;
    }

    private static SessionHello MapFromProto(ProtoSessionHello proto)
    {
        return new SessionHello
        {
            Peer = proto.Peer != null ? MapFromProto(proto.Peer) : null,
            Nonce = proto.Nonce,
            Proof = proto.Proof,
            UdpPort = proto.UdpPort,
            IsIntroducer = proto.IsIntroducer
        };
    }

    private static ProtoIntroduction MapToProto(Introduction introduction)
    {
        return new ProtoIntroduction
        {
            RequesterUserId = introduction.RequesterUserId ?? string.Empty,
            TargetUserId = introduction.TargetUserId ?? string.Empty,
            Token = introduction.Token ?? string.Empty,
            IntroducerUdpPort = introduction.IntroducerUdpPort
        };
    }

    private static Introduction MapFromProto(ProtoIntroduction proto)
    {
        return new Introduction
        {
            RequesterUserId = proto.RequesterUserId,
            TargetUserId = proto.TargetUserId,
            Token = proto.Token,
            IntroducerUdpPort = proto.IntroducerUdpPort
        };
    }
}
