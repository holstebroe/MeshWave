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
    public async Task<Manifest?> FetchManifestAsync(string address, int port, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeoutMs);

        using var client = new TcpClient();
        await client.ConnectAsync(address, port, cts.Token);

        var stream = client.GetStream();
        var request = new ManifestRequest { Type = ManifestRequestType.GetManifest };
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

            if (response?.ContentBytes == null || response.ContentBytes.Length == 0)
            {
                var reason = response?.Acknowledged == false
                    ? "Peer acknowledged the request but reported the content is not available."
                    : "Peer returned an empty response (content may not be hosted here).";
                return (null, reason);
            }

            return (response.ContentBytes, string.Empty);
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

