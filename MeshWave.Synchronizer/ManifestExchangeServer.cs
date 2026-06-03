using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Listens for incoming manifest exchange requests over TCP.
/// Handles: GetManifest, PushManifest, GetPeers (Peer Exchange / PEX).
/// All message sizes are enforced against SecurityLimits.
/// </summary>
public class ManifestExchangeServer : IDisposable
{
    public const int DefaultPort = 39877;

    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    private Func<Manifest?>? _localManifestProvider;
    private Func<IReadOnlyList<PeerInfo>>? _peersProvider;
    private Func<RendezvousRequest, RendezvousResponse?>? _rendezvousProvider;
    private Func<string, byte[]?>? _contentProvider;

    public ManifestExchangeServer(int port = DefaultPort)
    {
        _port = port;
    }

    public event EventHandler<ManifestReceivedEventArgs>? ManifestReceived;

    /// <summary>
    /// Starts the TCP server.
    /// </summary>
    /// <param name="localManifestProvider">Returns this peer's current manifest on demand.</param>
    /// <param name="peersProvider">Returns known peers for PEX responses. May be null to disable PEX serving.</param>
    /// <param name="rendezvousProvider">Optional bootstrap rendezvous provider for crossing-hands session issuance.</param>
    public async Task StartAsync(
        Func<Manifest?> localManifestProvider,
        Func<IReadOnlyList<PeerInfo>>? peersProvider = null,
        Func<RendezvousRequest, RendezvousResponse?>? rendezvousProvider = null,
        Func<string, byte[]?>? contentProvider = null,
        CancellationToken cancellationToken = default)
    {
        _localManifestProvider = localManifestProvider;
        _peersProvider = peersProvider;
        _rendezvousProvider = rendezvousProvider;
        _contentProvider = contentProvider;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        _serverTask = AcceptLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the server.
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_serverTask != null)
        {
            try { await _serverTask; } catch { }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_listener == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.ReceiveTimeout = SecurityLimits.ReadTimeoutMs;
            client.SendTimeout = SecurityLimits.ReadTimeoutMs;

            try
            {
                var stream = client.GetStream();
                var requestJson = await ReadMessageAsync(stream, ct);
                var request = JsonSerializer.Deserialize<ManifestRequest>(requestJson);
                if (request == null) return;

                switch (request.Type)
                {
                    case ManifestRequestType.GetManifest:
                    {
                        var manifest = _localManifestProvider?.Invoke();
                        if (manifest != null && (request.StartSequenceNumber > 0 || request.EndSequenceNumber != null))
                        {
                            var filteredOps = manifest.Operations
                                .Where(op => op.SequenceNumber >= request.StartSequenceNumber &&
                                            (request.EndSequenceNumber == null || op.SequenceNumber <= request.EndSequenceNumber))
                                .ToList();

                            manifest = new Manifest
                            {
                                UserId = manifest.UserId,
                                Version = manifest.Version,
                                LastUpdated = manifest.LastUpdated,
                                Snapshot = manifest.Snapshot,
                                Operations = filteredOps
                            };
                        }

                        var response = new ManifestResponse { Manifest = manifest };
                        await WriteMessageAsync(stream, JsonSerializer.Serialize(response), ct);
                        break;
                    }
                    case ManifestRequestType.PushManifest when request.Manifest != null:
                    {
                        if (request.Manifest.Operations.Count <= SecurityLimits.MaxManifestOperations)
                        {
                            var peerEndpoint = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
                            ManifestReceived?.Invoke(this, new ManifestReceivedEventArgs(request.Manifest, peerEndpoint, request.AnnouncingPeer));
                        }
                        var ack = new ManifestResponse { Acknowledged = true };
                        await WriteMessageAsync(stream, JsonSerializer.Serialize(ack), ct);
                        break;
                    }
                    case ManifestRequestType.GetPeers:
                    {
                        var peers = _peersProvider?.Invoke()
                            .Take(SecurityLimits.MaxPeersPerExchange)
                            .ToList() ?? [];
                        var response = new ManifestResponse { Peers = peers };
                        await WriteMessageAsync(stream, JsonSerializer.Serialize(response), ct);
                        break;
                    }
                    case ManifestRequestType.RequestRendezvous when request.Rendezvous != null:
                    {
                        var rendezvous = _rendezvousProvider?.Invoke(request.Rendezvous)
                            ?? new RendezvousResponse
                            {
                                Success = false,
                                Message = "Rendezvous is not enabled on this node."
                            };

                        var response = new ManifestResponse { Rendezvous = rendezvous, Acknowledged = rendezvous.Success };
                        await WriteMessageAsync(stream, JsonSerializer.Serialize(response), ct);
                        break;
                    }
                    case ManifestRequestType.RequestContent when !string.IsNullOrWhiteSpace(request.ContentHash):
                    {
                        var bytes = _contentProvider?.Invoke(request.ContentHash);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var response = new ManifestResponse
                            {
                                Acknowledged = true,
                                ContentLength = bytes.Length
                            };
                            await WriteMessageAsync(stream, JsonSerializer.Serialize(response), ct);
                            await stream.WriteAsync(bytes, ct);
                            await stream.FlushAsync(ct);
                        }
                        else
                        {
                            var response = new ManifestResponse { Acknowledged = false };
                            await WriteMessageAsync(stream, JsonSerializer.Serialize(response), ct);
                        }
                        break;
                    }
                    default:
                    {
                        var response = new ManifestResponse { Acknowledged = false };
                        await WriteMessageAsync(stream, JsonSerializer.Serialize(response), ct);
                        break;
                    }
                }
            }
            catch { /* ignore per-client errors */ }
        }
    }

    internal static async Task WriteMessageAsync(Stream stream, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var lengthBytes = BitConverter.GetBytes(body.Length);
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    internal static async Task<string> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, ct);
        var length = BitConverter.ToInt32(lengthBytes);

        if (length <= 0 || length > SecurityLimits.MaxMessageBytes)
            throw new InvalidDataException($"Rejected message: length {length} exceeds limit.");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        return Encoding.UTF8.GetString(body);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}

public class ManifestReceivedEventArgs(Manifest manifest, string peerAddress, PeerInfo? announcingPeer) : EventArgs
{
    public Manifest Manifest { get; } = manifest;
    public string PeerAddress { get; } = peerAddress;
    public PeerInfo? AnnouncingPeer { get; } = announcingPeer;
}

public enum ManifestRequestType
{
    GetManifest,
    PushManifest,
    GetPeers,
    RequestRendezvous,
    RequestContent
}

public class ManifestRequest
{
    public ManifestRequestType Type { get; set; }
    public Manifest? Manifest { get; set; }
    public RendezvousRequest? Rendezvous { get; set; }
    public string? ContentHash { get; set; }
    public PeerInfo? AnnouncingPeer { get; set; }
    public int StartSequenceNumber { get; set; }
    public int? EndSequenceNumber { get; set; }
}

public class ManifestResponse
{
    public Manifest? Manifest { get; set; }
    public bool Acknowledged { get; set; }
    public List<PeerInfo> Peers { get; set; } = [];
    public RendezvousResponse? Rendezvous { get; set; }
    public byte[]? ContentBytes { get; set; }
    public long? ContentLength { get; set; }
}

public class RendezvousRequest
{
    public string InitiatorUserId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public int InitiatorPort { get; set; }
    public int RequestedProbeWindowMs { get; set; } = 4_000;
}

public class RendezvousResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime ProbeStartUtc { get; set; }
    public int ProbeWindowMs { get; set; } = 4_000;
    public string Message { get; set; } = string.Empty;
}
