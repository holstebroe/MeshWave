using MeshWave.Common.Core;
using System.Net.Sockets;
using System.Text;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using NLog;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MeshWave.Synchronizer;

/// <summary>
/// Sends manifest exchange requests to remote nodes. A request goes over an existing persistent
/// <see cref="PeerSession"/> when <see cref="SessionResolver"/> finds one (this is the only way to reach a peer
/// without an open port); otherwise it opens a one-shot TCP connection to the node's listening port.
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
    /// Finds a live session for a peer, given its UserId (may be null) and its address and port.
    /// Set by the owner of the <see cref="PeerSessionManager"/>.
    /// </summary>
    public Func<string?, string, int, PeerSession?>? SessionResolver { get; set; }

    /// <summary>
    /// Fetches the manifest from a remote peer, calculating delta synchronization automatically.
    /// </summary>
    public Task<Manifest?> FetchManifestAsync(
        string address,
        int port,
        IManifestStore store,
        string targetUserId,
        ManifestStreamType streamType = ManifestStreamType.Content,
        CancellationToken cancellationToken = default)
    {
        var existing = store.Get(targetUserId, streamType);
        var startSeq = ManifestManager.GetHeadSequenceNumber(existing) + 1;
        return FetchManifestCoreAsync(targetUserId, address, port, streamType, startSeq, null, cancellationToken);
    }

    /// <summary>
    /// Fetches the manifest delta from a peer, over its session if there is one.
    /// </summary>
    public Task<Manifest?> FetchManifestAsync(PeerInfo peer, IManifestStore store, ManifestStreamType streamType, CancellationToken cancellationToken = default)
    {
        return FetchManifestAsync(peer.Address, peer.Port, store, peer.UserId, streamType, cancellationToken);
    }

    /// <summary>
    /// Fetches the manifest from a remote peer.
    /// Returns null if the peer returns no manifest; throws if it is unreachable.
    /// </summary>
    public Task<Manifest?> FetchManifestAsync(
        string address,
        int port,
        ManifestStreamType streamType = ManifestStreamType.Content,
        int startSequenceNumber = 0,
        int? endSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        return FetchManifestCoreAsync(null, address, port, streamType, startSequenceNumber, endSequenceNumber, cancellationToken);
    }

    private async Task<Manifest?> FetchManifestCoreAsync(string? userId, string address, int port, ManifestStreamType streamType, int startSequenceNumber, int? endSequenceNumber, CancellationToken cancellationToken)
    {
        _logger.Debug("Fetching {0} manifest from {1}:{2} (start={3}, end={4})", streamType, address, port, startSequenceNumber, endSequenceNumber);
        var response = await ExchangeAsync(userId, address, port, new ManifestRequest
        {
            Type = ManifestRequestType.GetManifest,
            StreamType = streamType,
            StartSequenceNumber = startSequenceNumber,
            EndSequenceNumber = endSequenceNumber
        }, cancellationToken);

        _logger.Debug("FetchManifest from {0}:{1} outcome: {2} ops", address, port, response?.Manifest?.Operations.Count ?? 0);
        return response?.Manifest;
    }

    /// <summary>
    /// Pushes our manifest to a remote peer.
    /// </summary>
    public Task<bool> PushManifestAsync(string address, int port, Manifest manifest, CancellationToken cancellationToken = default)
    {
        return PushManifestCoreAsync(null, address, port, manifest, announcingPeer: null, cancellationToken);
    }

    /// <summary>
    /// Pushes our manifest to a remote peer and includes explicit local peer metadata.
    /// </summary>
    public Task<bool> PushManifestAsync(string address, int port, Manifest manifest, PeerInfo announcingPeer, CancellationToken cancellationToken = default)
    {
        return PushManifestCoreAsync(null, address, port, manifest, announcingPeer, cancellationToken);
    }

    /// <summary>
    /// Pushes our manifest to a peer, over its session if there is one.
    /// </summary>
    public Task<bool> PushManifestAsync(PeerInfo peer, Manifest manifest, PeerInfo announcingPeer, CancellationToken cancellationToken = default)
    {
        return PushManifestCoreAsync(peer.UserId, peer.Address, peer.Port, manifest, announcingPeer, cancellationToken);
    }

    private async Task<bool> PushManifestCoreAsync(string? userId, string address, int port, Manifest manifest, PeerInfo? announcingPeer, CancellationToken cancellationToken)
    {
        _logger.Debug("Pushing {0} manifest for {1} to {2}:{3}", manifest.StreamType, manifest.UserId, address, port);
        var response = await ExchangeAsync(userId, address, port, new ManifestRequest
        {
            Type = ManifestRequestType.PushManifest,
            StreamType = manifest.StreamType,
            Manifest = manifest,
            AnnouncingPeer = announcingPeer
        }, cancellationToken);

        _logger.Debug("PushManifest to {0}:{1} outcome: {2}", address, port, response?.Acknowledged == true);
        return response?.Acknowledged == true;
    }

    /// <summary>
    /// Requests the peer's known peer list (Peer Exchange / PEX).
    /// Returns null if the peer is unreachable or an empty list if the peer does not support PEX.
    /// </summary>
    public Task<IReadOnlyList<PeerInfo>?> FetchPeersAsync(string address, int port, string? customLabel = null, CancellationToken cancellationToken = default)
    {
        return FetchPeersCoreAsync(null, address, port, customLabel, cancellationToken);
    }

    /// <summary>
    /// Requests a peer's known peer list, over its session if there is one.
    /// </summary>
    public Task<IReadOnlyList<PeerInfo>?> FetchPeersAsync(PeerInfo peer, CancellationToken cancellationToken = default)
    {
        return FetchPeersCoreAsync(peer.UserId, peer.Address, peer.Port, null, cancellationToken);
    }

    private async Task<IReadOnlyList<PeerInfo>?> FetchPeersCoreAsync(string? userId, string address, int port, string? customLabel, CancellationToken cancellationToken)
    {
        var label = customLabel ?? "peers";
        try
        {
            _logger.Debug("Fetching {0} from {1}:{2} (PEX)", label, address, port);
            var response = await ExchangeAsync(userId, address, port, new ManifestRequest { Type = ManifestRequestType.GetPeers }, cancellationToken);
            if (response == null) return null;

            var peers = response.Peers
                .Take(SecurityLimits.MaxPeersPerExchange)
                .ToList();
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
    /// Registers this peer with a bootstrap node or peer (<see cref="ManifestRequestType.Announce"/>).
    /// Returns the peer info the node reports about itself (empty for a standalone bootstrap),
    /// or null if the node is unreachable.
    /// </summary>
    public async Task<IReadOnlyList<PeerInfo>?> AnnounceAsync(string address, int port, PeerInfo self, CancellationToken cancellationToken = default)
    {
        var result = await AnnounceWithResultAsync(address, port, self, cancellationToken);
        return result?.Peers;
    }

    /// <summary>
    /// Registers this peer with a node and returns the full outcome: the node's own peer info, the address it observed
    /// for us, and whether it could connect back to our announced port. Returns null if the node is unreachable.
    /// </summary>
    public async Task<AnnounceResult?> AnnounceWithResultAsync(string address, int port, PeerInfo self, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ExchangeAsync(null, address, port, new ManifestRequest { Type = ManifestRequestType.Announce, AnnouncingPeer = self }, cancellationToken);

            _logger.Debug("Announce to {0}:{1} outcome: {2}", address, port, response?.Acknowledged == true);
            if (response?.Acknowledged != true)
                return null;

            return new AnnounceResult(
                response.Peers.Take(SecurityLimits.MaxPeersPerExchange).ToList(),
                response.ObservedAddress,
                response.DialBackSucceeded);
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to announce to {0}:{1}: {2}", address, port, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Requests raw content bytes from a peer by content hash over a one-shot connection.
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

            var response = await ReadResponseAsync(stream, cts.Token);
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
    /// Requests a content stream from a peer by content hash over a one-shot connection.
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

            var response = await ReadResponseAsync(stream, cts.Token);
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

    /// <summary>
    /// Requests a specific chunk of content bytes from a peer by content hash.
    /// Returns the chunk bytes, the total content length, and a human-readable failure reason.
    /// </summary>
    public Task<(byte[]? Bytes, long TotalLength, string FailureReason)> RequestContentChunkAsync(string address, int port, string contentHash, long offset, long length, CancellationToken cancellationToken = default)
    {
        return RequestContentChunkCoreAsync(null, address, port, contentHash, offset, length, cancellationToken);
    }

    /// <summary>
    /// Requests a chunk of content from a peer, over its session if there is one.
    /// </summary>
    public Task<(byte[]? Bytes, long TotalLength, string FailureReason)> RequestContentChunkAsync(PeerInfo peer, string contentHash, long offset, long length, CancellationToken cancellationToken = default)
    {
        return RequestContentChunkCoreAsync(peer.UserId, peer.Address, peer.Port, contentHash, offset, length, cancellationToken);
    }

    private async Task<(byte[]? Bytes, long TotalLength, string FailureReason)> RequestContentChunkCoreAsync(string? userId, string address, int port, string contentHash, long offset, long length, CancellationToken cancellationToken)
    {
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.RequestContent,
            ContentHash = contentHash,
            ChunkOffset = offset,
            ChunkLength = length
        };

        try
        {
            var session = SessionResolver?.Invoke(userId, address, port);
            if (session != null)
            {
                var sessionResponse = await session.RequestAsync(request, _timeoutMs, cancellationToken);
                if (sessionResponse?.Acknowledged != true)
                    return (null, 0, sessionResponse == null ? "Session request failed." : "Peer reported the content is not available.");

                var bytes = sessionResponse.ContentBytes ?? [];
                if (bytes.Length != sessionResponse.ContentLength)
                    return (null, 0, "Peer returned a truncated chunk.");
                return (bytes, sessionResponse.TotalContentLength ?? sessionResponse.ContentLength, string.Empty);
            }

            if (port <= 0)
                return (null, 0, "Peer has no open port and no session.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeoutMs);

            using var client = new TcpClient();
            _logger.Debug("Connecting to {0}:{1} for chunk...", address, port);
            await client.ConnectAsync(address, port, cts.Token);

            _logger.Debug("Requesting chunk for content {0} from {1}:{2} (offset: {3}, length: {4})", contentHash, address, port, offset, length);
            var stream = client.GetStream();
            await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);

            var response = await ReadResponseAsync(stream, cts.Token);
            if (response?.Acknowledged != true)
            {
                var reason = response?.Acknowledged == false
                    ? "Peer acknowledged the chunk request but reported the content is not available."
                    : "Peer returned an empty response (content may not be hosted here).";
                return (null, 0, reason);
            }

            // A zero-length chunk might just mean we requested past the end, or a 0-byte request to get length
            var chunkBytes = new byte[response.ContentLength];
            if (response.ContentLength > 0)
            {
                await stream.ReadExactlyAsync(chunkBytes, cts.Token);
            }

            long totalLength = response.TotalContentLength ?? response.ContentLength;
            return (chunkBytes, totalLength, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (null, 0, "Connection timed out.");
        }
        catch (Exception ex)
        {
            return (null, 0, ex.Message);
        }
    }

    /// <summary>
    /// Sends a request and returns the response. Uses a session when one is known for the peer, otherwise a one-shot
    /// TCP connection. Throws if the peer cannot be reached (no session and no open port, or the connection fails).
    /// </summary>
    private async Task<ManifestResponse?> ExchangeAsync(string? userId, string address, int port, ManifestRequest request, CancellationToken cancellationToken)
    {
        var session = SessionResolver?.Invoke(userId, address, port);
        if (session != null)
        {
            var response = await session.RequestAsync(request, _timeoutMs, cancellationToken);
            if (response != null) return response;
            if (port <= 0) throw new IOException($"Session request {request.Type} to {userId ?? address} failed.");
            // Fall back to a one-shot connection if the session broke.
        }

        if (port <= 0)
            throw new InvalidOperationException($"Peer {userId ?? address} has no open port and no session.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        _logger.Trace("Connecting to {0}:{1}...", address, port);
        await client.ConnectAsync(address, port, cts.Token);

        var stream = client.GetStream();
        await ManifestExchangeServer.WriteMessageAsync(stream, request, cts.Token);
        return await ReadResponseAsync(stream, cts.Token);
    }

    private static async Task<ManifestResponse?> ReadResponseAsync(Stream stream, CancellationToken ct)
    {
        var (bytes, isJson) = await ManifestExchangeServer.ReadMessageAsync(stream, ct);
        return isJson
            ? JsonSerializer.Deserialize<ManifestResponse>(Encoding.UTF8.GetString(bytes))
            : ManifestSerializer.DeserializeResponse(bytes);
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
}

/// <summary>Outcome of an <see cref="ManifestRequestType.Announce"/>.</summary>
/// <param name="Peers">The node's own peer info (empty for a standalone bootstrap).</param>
/// <param name="ObservedAddress">Our source address as the node saw it.</param>
/// <param name="DialBackSucceeded">Whether the node could connect back to our announced port; null if it did not check.</param>
public record AnnounceResult(IReadOnlyList<PeerInfo> Peers, string? ObservedAddress, bool? DialBackSucceeded);
