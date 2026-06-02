using System.Net.Sockets;
using System.Text.Json;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Connects to a remote peer and exchanges manifests or peer lists.
/// </summary>
public class ManifestExchangeClient
{
    private readonly int _timeoutMs;

    public ManifestExchangeClient(int timeoutMs = 10_000)
    {
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Fetches the manifest from a remote peer.
    /// Returns null if the peer is unreachable or returns no manifest.
    /// </summary>
    public async Task<Manifest?> FetchManifestAsync(
        string address,
        int port,
        int startSequenceNumber = 0,
        int? endSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        await client.ConnectAsync(address, port, cts.Token);

        var stream = client.GetStream();
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.GetManifest,
            StartSequenceNumber = startSequenceNumber,
            EndSequenceNumber = endSequenceNumber
        };
        await ManifestExchangeServer.WriteMessageAsync(stream, JsonSerializer.Serialize(request), cts.Token);

        var responseJson = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        var response = JsonSerializer.Deserialize<ManifestResponse>(responseJson);
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

    private async Task<bool> PushManifestCoreAsync(string address, int port, Manifest manifest, PeerInfo? announcingPeer, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        await client.ConnectAsync(address, port, cts.Token);

        var stream = client.GetStream();

        var request = new ManifestRequest
        {
            Type = ManifestRequestType.PushManifest,
            Manifest = manifest,
            AnnouncingPeer = announcingPeer
        };
        await ManifestExchangeServer.WriteMessageAsync(stream, JsonSerializer.Serialize(request), cts.Token);

        var responseJson = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        var response = JsonSerializer.Deserialize<ManifestResponse>(responseJson);
        return response?.Acknowledged == true;
    }

    /// <summary>
    /// Requests the peer's known peer list (Peer Exchange / PEX).
    /// Returns an empty list if the peer does not support PEX or is unreachable.
    /// </summary>
    public async Task<IReadOnlyList<PeerInfo>> FetchPeersAsync(string address, int port, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(address, port, cts.Token);

            var stream = client.GetStream();
            var request = new ManifestRequest { Type = ManifestRequestType.GetPeers };
            await ManifestExchangeServer.WriteMessageAsync(stream, JsonSerializer.Serialize(request), cts.Token);

            var responseJson = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            var response = JsonSerializer.Deserialize<ManifestResponse>(responseJson);
            return response?.Peers
                .Take(SecurityLimits.MaxPeersPerExchange)
                .ToList() ?? [];
        }
        catch
        {
            return [];
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
            await client.ConnectAsync(address, port, cts.Token);

            var stream = client.GetStream();
            var request = new ManifestRequest { Type = ManifestRequestType.RequestContent, ContentHash = contentHash };
            await ManifestExchangeServer.WriteMessageAsync(stream, JsonSerializer.Serialize(request), cts.Token);

            var responseJson = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            var response = JsonSerializer.Deserialize<ManifestResponse>(responseJson);

            if (response?.Acknowledged != true || response.ContentLength <= 0)
            {
                var reason = response?.Acknowledged == false
                    ? "Peer acknowledged the request but reported the content is not available."
                    : "Peer returned an empty response (content may not be hosted here).";
                return (null, reason);
            }

            var bytes = new byte[response.ContentLength];
            await stream.ReadExactlyAsync(bytes, cts.Token);
            return (bytes, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (null, $"Request timed out after {_timeoutMs / 1000}s connecting to {address}:{port}.");
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

            await client.ConnectAsync(address, port, cts.Token);
            var stream = client.GetStream();

            var request = new ManifestRequest { Type = ManifestRequestType.RequestContent, ContentHash = contentHash };
            await ManifestExchangeServer.WriteMessageAsync(stream, JsonSerializer.Serialize(request), cts.Token);

            var responseJson = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            var response = JsonSerializer.Deserialize<ManifestResponse>(responseJson);

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
        public override void Flush() => _stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _stream.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
        public override void SetLength(long value) => _stream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

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
        await client.ConnectAsync(address, port, cts.Token);

        var stream = client.GetStream();
        var request = new ManifestRequest { Type = ManifestRequestType.RequestRendezvous, Rendezvous = rendezvous };
        await ManifestExchangeServer.WriteMessageAsync(stream, JsonSerializer.Serialize(request), cts.Token);

        var responseJson = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
        var response = JsonSerializer.Deserialize<ManifestResponse>(responseJson);
        return response?.Rendezvous;
    }
}

