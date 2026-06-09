using MeshWave.Common.Core;
using System.Net.Sockets;
using System.Text;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using NLog;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MeshWave.Synchronizer;

/// <summary>
/// Connects to a remote peer and exchanges manifests or peer lists.
/// </summary>
public class ManifestExchangeClient
{
    private readonly Logger _logger;
    private readonly int _timeoutMs;

    public ManifestExchangeClient(int timeoutMs = 10_000, Logger? logger = null)
    {
        _timeoutMs = timeoutMs;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    /// <summary>
    /// Fetches the manifest from a remote peer, calculating delta synchronization automatically.
    /// </summary>
    public async Task<Manifest?> FetchManifestAsync(
        string address,
        int port,
        IManifestStore store,
        string targetUserId,
        ManifestStreamType streamType = ManifestStreamType.Content,
        CancellationToken cancellationToken = default)
    {
        var existing = store.Get(targetUserId, streamType);
        var startSeq = (existing?.Snapshot?.LastSequenceNumber ?? -1) + 1 + (existing?.Operations.Count ?? 0);

        var isBootstrap = address.Contains("bootstrap") || targetUserId.StartsWith("bootstrap:");
        var relayUserId = isBootstrap && !targetUserId.StartsWith("bootstrap:") ? targetUserId : null;

        return await FetchManifestAsync(address, port, streamType, startSeq, null, relayUserId, cancellationToken);
    }

    /// <summary>
    /// Fetches the manifest from a remote peer.
    /// Returns null if the peer is unreachable or returns no manifest.
    /// </summary>
    public async Task<Manifest?> FetchManifestAsync(
        string address,
        int port,
        ManifestStreamType streamType = ManifestStreamType.Content,
        int startSequenceNumber = 0,
        int? endSequenceNumber = null,
        string? targetUserId = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        _logger.Debug("Connecting to {0}:{1}...", address, port);
        await client.ConnectAsync(address, port, cts.Token);

        _logger.Debug("Fetching {0} manifest from {1}:{2} (start={3}, end={4}, target={5})", streamType, address, port, startSequenceNumber, endSequenceNumber, targetUserId ?? "direct");
        var stream = client.GetStream();
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.GetManifest,
            StreamType = streamType,
            StartSequenceNumber = startSequenceNumber,
            EndSequenceNumber = endSequenceNumber,
            TargetUserId = targetUserId
        };
        await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

        var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        ManifestResponse? response;
        if (isJson)
        {
            var json = Encoding.UTF8.GetString(bytes);
            response = JsonSerializer.Deserialize<ManifestResponse>(json);
        }
        else
        {
            response = ManifestSerializer.DeserializeResponse(bytes);
        }

        _logger.Debug("FetchManifest from {0}:{1} outcome: {2} ops", address, port, response?.Manifest?.Operations.Count ?? 0);
        return response?.Manifest;
    }

    /// <summary>
    /// Pushes our manifest to a remote peer.
    /// </summary>
    public Task<bool> PushManifestAsync(string address, int port, Manifest manifest, CancellationToken cancellationToken = default)
    {
        return PushManifestCoreAsync(address, port, manifest, announcingPeer: null, cancellationToken);
    }

    /// <summary>
    /// Pushes our manifest to a remote peer and includes explicit local peer metadata.
    /// </summary>
    public Task<bool> PushManifestAsync(string address, int port, Manifest manifest, PeerInfo announcingPeer, CancellationToken cancellationToken = default)
    {
        return PushManifestCoreAsync(address, port, manifest, announcingPeer, cancellationToken);
    }

    /// <summary>
    /// Pushes our manifest to a bootstrap node for relaying to NATed followers.
    /// </summary>
    public async Task<bool> RelayManifestPushAsync(string address, int port, Manifest manifest, PeerInfo announcingPeer, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        _logger.Debug("Connecting to {0}:{1}...", address, port);
        await client.ConnectAsync(address, port, cts.Token);

        _logger.Debug("Relaying manifest push for {0} to {1}:{2} via bootstrap", manifest.UserId, address, port);
        var stream = client.GetStream();

        var request = new ManifestRequest
        {
            Type = ManifestRequestType.RelayManifestPush,
            StreamType = manifest.StreamType,
            Manifest = manifest,
            AnnouncingPeer = announcingPeer
        };
        await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

        var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        ManifestResponse? response;
        if (isJson)
        {
            var json = Encoding.UTF8.GetString(bytes);
            response = JsonSerializer.Deserialize<ManifestResponse>(json);
        }
        else
        {
            response = ManifestSerializer.DeserializeResponse(bytes);
        }

        _logger.Debug("RelayManifestPush to {0}:{1} outcome: {2}", address, port, response?.Acknowledged == true);
        return response?.Acknowledged == true;
    }

    private async Task<bool> PushManifestCoreAsync(string address, int port, Manifest manifest, PeerInfo? announcingPeer, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        _logger.Debug("Connecting to {0}:{1}...", address, port);
        await client.ConnectAsync(address, port, cts.Token);

        _logger.Debug($"Pushing {manifest.StreamType} manifest for {manifest.UserId} to {address}:{port}");
        var stream = client.GetStream();

        var request = new ManifestRequest
        {
            Type = ManifestRequestType.PushManifest,
            StreamType = manifest.StreamType,
            Manifest = manifest,
            AnnouncingPeer = announcingPeer
        };
        await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

        var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        ManifestResponse? response;
        if (isJson)
        {
            var json = Encoding.UTF8.GetString(bytes);
            response = JsonSerializer.Deserialize<ManifestResponse>(json);
        }
        else
        {
            response = ManifestSerializer.DeserializeResponse(bytes);
        }

        _logger.Debug("PushManifest to {0}:{1} outcome: {2}", address, port, response?.Acknowledged == true);
        return response?.Acknowledged == true;
    }

    /// <summary>
    /// Requests the peer's known peer list (Peer Exchange / PEX).
    /// Returns null if the peer is unreachable or an empty list if the peer does not support PEX.
    /// </summary>
    public async Task<IReadOnlyList<PeerInfo>?> FetchPeersAsync(string address, int port, string? customLabel = null, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        var label = customLabel ?? "peers";

        try
        {
            using var client = new TcpClient();
            _logger.Debug("Connecting to {0} {1}:{2}...", label, address, port);
            await client.ConnectAsync(address, port, cts.Token);

            _logger.Debug("Fetching {0} from {1}:{2} (PEX)", label, address, port);
            var stream = client.GetStream();
            var request = new ManifestRequest { Type = ManifestRequestType.GetPeers };
            await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

            var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            ManifestResponse? response;
            if (isJson)
            {
                var json = Encoding.UTF8.GetString(bytes);
                response = JsonSerializer.Deserialize<ManifestResponse>(json);
            }
            else
            {
                response = ManifestSerializer.DeserializeResponse(bytes);
            }

            var peers = response?.Peers
                .Take(SecurityLimits.MaxPeersPerExchange)
                .ToList() ?? [];
            _logger.Debug("Fetched {0} peers from {1}:{2}", peers.Count, address, port);
            return peers;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Failed to fetch {0} from {1}:{2}: The operation timed out after {3}ms.", label, address, port, _timeoutMs);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to fetch {0} from {1}:{2}: {3}", label, address, port, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Requests raw content bytes from a peer by content hash.
    /// Returns null bytes if the peer does not have the content or is unreachable,
    /// along with a human-readable failure reason.
    /// </summary>
    public async Task<(byte[]? Bytes, string FailureReason)> RequestContentAsync(string address, int port, string contentHash, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        try
        {
            using var client = new TcpClient();
            _logger.Debug("Connecting to {0}:{1}...", address, port);
            await client.ConnectAsync(address, port, cts.Token);

            _logger.Debug("Requesting content {0} from {1}:{2}", contentHash, address, port);
            var stream = client.GetStream();
            var request = new ManifestRequest { Type = ManifestRequestType.RequestContent, ContentHash = contentHash };
            await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

            var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            ManifestResponse? response;
            if (isJson)
            {
                var json = Encoding.UTF8.GetString(bytes);
                response = JsonSerializer.Deserialize<ManifestResponse>(json);
            }
            else
            {
                response = ManifestSerializer.DeserializeResponse(bytes);
            }

            if (response?.Acknowledged != true || response.ContentLength <= 0)
            {
                var reason = response?.Acknowledged == false
                    ? "Peer acknowledged the request but reported the content is not available."
                    : "Peer returned an empty response (content may not be hosted here).";
                return (null, reason);
            }

            var contentBytes = new byte[response.ContentLength];
            await stream.ReadExactlyAsync(contentBytes, cts.Token);
            return (contentBytes, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (null, $"Request timed out after {_timeoutMs}ms connecting to {address}:{port}.");
        }
        catch (SocketException ex)
        {
            return (null, $"TCP connection to {address}:{port} failed: {ex.SocketErrorCode} – {ex.Message}");
        }
        catch (Exception ex)
        {
            return (null, $"Unexpected error requesting content from {address}:{port}: {ex.Message}");
        }
    }

    /// <summary>
    /// Requests a content stream from a peer by content hash.
    /// The returned stream must be disposed by the caller, which also closes the underlying TCP connection.
    /// </summary>
    public async Task<(Stream? Stream, long ContentLength, string FailureReason)> RequestContentStreamAsync(string address, int port, string contentHash, CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeoutMs);

            _logger.Debug("Connecting to {0}:{1}...", address, port);
            await client.ConnectAsync(address, port, cts.Token);

            _logger.Info("Requesting content stream for hash {0} from {1}:{2}", contentHash, address, port);
            var stream = client.GetStream();

            var request = new ManifestRequest { Type = ManifestRequestType.RequestContent, ContentHash = contentHash };
            await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

            var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            ManifestResponse? response;
            if (isJson)
            {
                var json = Encoding.UTF8.GetString(bytes);
                response = JsonSerializer.Deserialize<ManifestResponse>(json);
            }
            else
            {
                response = ManifestSerializer.DeserializeResponse(bytes);
            }

            if (response?.Acknowledged != true || response.ContentLength <= 0)
            {
                client.Dispose();
                var reason = response?.Acknowledged == false
                    ? "Peer acknowledged the request but reported the content is not available."
                    : "Peer returned an empty response (content may not be hosted here).";
                return (null, 0, reason);
            }

            // Return a wrapper stream that disposes the TcpClient when closed
            return (new TcpClientStreamWrapper(client, stream, response.ContentLength), response.ContentLength, string.Empty);
        }
        catch (Exception ex)
        {
            client.Dispose();
            return (null, 0, ex.Message);
        }
    }

    private class TcpClientStreamWrapper : Stream
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly long _length;

        public TcpClientStreamWrapper(TcpClient client, NetworkStream stream, long length)
        {
            _client = client;
            _stream = stream;
            _length = length;
        }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _length;
        public override long Position { get => _stream.Position; set => _stream.Position = value; }
        public override void Flush()
        {
            _stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
                _client.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public async Task<RendezvousResponse?> RequestRendezvousAsync(string address, int port, RendezvousRequest rendezvous, CancellationToken cancellationToken = default)
    {
        if (rendezvous == null)
            return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        _logger.Debug("Connecting to {0}:{1}...", address, port);
        await client.ConnectAsync(address, port, cts.Token);

        _logger.Debug("Requesting rendezvous from {0}:{1} for target {2}", address, port, rendezvous.TargetUserId);
        var stream = client.GetStream();
        var request = new ManifestRequest { Type = ManifestRequestType.RequestRendezvous, Rendezvous = rendezvous };
        await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

        var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        ManifestResponse? response;
        if (isJson)
        {
            var json = Encoding.UTF8.GetString(bytes);
            response = JsonSerializer.Deserialize<ManifestResponse>(json);
        }
        else
        {
            response = ManifestSerializer.DeserializeResponse(bytes);
        }

        _logger.Debug("Rendezvous response from {0}:{1} outcome: {2}", address, port, response?.Rendezvous?.Success == true);
        return response?.Rendezvous;
    }
}
