using MeshWave.Common.Core;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using NLog;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Logger = NLog.Logger;

namespace MeshWave.Synchronizer;

/// <summary>
/// Listens for incoming manifest exchange requests over TCP.
/// Handles: GetManifest, PushManifest, GetPeers (Peer Exchange / PEX).
/// All message sizes are enforced against SecurityLimits.
/// </summary>
public class ManifestExchangeServer : IDisposable
{
    private readonly Logger _logger;
    public const int DefaultPort = 39877;

    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    private Func<ManifestStreamType, Manifest?>? _localManifestProvider;
    private Func<IReadOnlyList<PeerInfo>>? _peersProvider;
    private Func<RendezvousRequest, RendezvousResponse?>? _rendezvousProvider;
    private Func<string, byte[]?>? _contentProvider;
    private Func<string, ManifestStreamType, Manifest?>? _relayedManifestProvider;

    public ManifestExchangeServer(int port = DefaultPort, Logger? logger = null)
    {
        _port = port;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public event EventHandler<ManifestReceivedEventArgs>? ManifestReceived;
    public event EventHandler<NotifyNewOperationEventArgs>? NotificationReceived;

    /// <summary>
    /// Starts the TCP server.
    /// </summary>
    /// <param name="localManifestProvider">Returns this peer's current manifest on demand for a given stream.</param>
    /// <param name="peersProvider">Returns known peers for PEX responses. May be null to disable PEX serving.</param>
    /// <param name="rendezvousProvider">Optional bootstrap rendezvous provider for crossing-hands session issuance.</param>
    public async Task StartAsync(
        Func<ManifestStreamType, Manifest?> localManifestProvider,
        Func<IReadOnlyList<PeerInfo>>? peersProvider = null,
        Func<RendezvousRequest, RendezvousResponse?>? rendezvousProvider = null,
        Func<string, byte[]?>? contentProvider = null,
        Func<string, ManifestStreamType, Manifest?>? relayedManifestProvider = null,
        CancellationToken cancellationToken = default)
    {
        _localManifestProvider = localManifestProvider;
        _peersProvider = peersProvider;
        _rendezvousProvider = rendezvousProvider;
        _contentProvider = contentProvider;
        _relayedManifestProvider = relayedManifestProvider;
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
            try { await _serverTask; } catch { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_listener == null) return;

        while (!ct.IsCancellationRequested)
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.Debug("Accepted connection from {0}", remoteEndpoint);

        using (client)
        {
            client.ReceiveTimeout = SecurityLimits.ReadTimeoutMs;
            client.SendTimeout = SecurityLimits.ReadTimeoutMs;

            try
            {
                var stream = client.GetStream();
                var (bytes, isJson) = await ReadMessageAsync(stream, ct);

                ManifestRequest? request;
                if (isJson)
                {
                    var json = Encoding.UTF8.GetString(bytes);
                    request = JsonSerializer.Deserialize<ManifestRequest>(json);
                }
                else
                {
                    request = ManifestSerializer.DeserializeRequest(bytes);
                }

                if (request == null)
                {
                    _logger.Warn("Received empty or invalid request from {0}", remoteEndpoint);
                    return;
                }

                _logger.Debug("Received {0} request from {1} (format={2})", request.Type, remoteEndpoint, isJson ? "JSON" : "Protobuf");

                switch (request.Type)
                {
                    case ManifestRequestType.GetManifest:
                    {
                        var originalManifest = !string.IsNullOrWhiteSpace(request.TargetUserId)
                            ? _relayedManifestProvider?.Invoke(request.TargetUserId, request.StreamType)
                            : _localManifestProvider?.Invoke(request.StreamType);

                        Manifest? responseManifest = null;
                        if (originalManifest == null)
                        {
                            _logger.Warn("Could not provide manifest for {0} (TargetUserId: {1})",
                                remoteEndpoint, request.TargetUserId ?? "local");
                        }
                        else
                        {
                            _logger.Info("Serving manifest for {0} to {1} (delta={2}, ops={3})",
                                originalManifest.UserId, remoteEndpoint, request.StartSequenceNumber > 0, originalManifest.Operations.Count);

                            lock (originalManifest)
                            {
                                var snapshot = originalManifest.Snapshot;
                                if (request.StartSequenceNumber > (snapshot?.LastSequenceNumber ?? -1)) snapshot = null;

                                var filteredOps = originalManifest.Operations
                                    .Where(op => op.SequenceNumber >= request.StartSequenceNumber &&
                                                (request.EndSequenceNumber == null || op.SequenceNumber <= request.EndSequenceNumber))
                                    .ToList();

                                responseManifest = new Manifest
                                {
                                    UserId = originalManifest.UserId,
                                    StreamType = originalManifest.StreamType,
                                    Version = originalManifest.Version,
                                    LastUpdated = originalManifest.LastUpdated,
                                    Snapshot = snapshot,
                                    Operations = filteredOps
                                };
                            }
                        }

                        var response = new ManifestResponse { Manifest = responseManifest };
                        await WriteMessageAsync(stream, response, ct);
                        break;
                    }
                    case ManifestRequestType.PushManifest when request.Manifest != null:
                    {
                        var opCount = request.Manifest.Operations.Count;
                        if (opCount <= SecurityLimits.MaxManifestOperations)
                        {
                            var peerEndpoint = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
                            _logger.Info("Received manifest push from {0} (User: {1}, Ops: {2})",
                                remoteEndpoint, request.Manifest.UserId, opCount);
                            ManifestReceived?.Invoke(this, new ManifestReceivedEventArgs(request.Manifest, peerEndpoint, request.AnnouncingPeer, isRelay: false));
                        }
                        else
                        {
                            _logger.Warn("Rejected push from {0}: too many operations ({1})", remoteEndpoint, opCount);
                        }
                        var ack = new ManifestResponse { Acknowledged = true };
                        await WriteMessageAsync(stream, ack, ct);
                        break;
                    }
                    case ManifestRequestType.RelayManifestPush when request.Manifest != null:
                    {
                        var opCount = request.Manifest.Operations.Count;
                        if (opCount <= SecurityLimits.MaxManifestOperations)
                        {
                            var peerEndpoint = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
                            _logger.Info("Received relayed manifest push from {0} (User: {1}, Ops: {2})",
                                remoteEndpoint, request.Manifest.UserId, opCount);
                            ManifestReceived?.Invoke(this, new ManifestReceivedEventArgs(request.Manifest, peerEndpoint, request.AnnouncingPeer, isRelay: true));
                        }
                        else
                        {
                            _logger.Warn("Rejected relayed push from {0}: too many operations ({1})", remoteEndpoint, opCount);
                        }
                        var ack = new ManifestResponse { Acknowledged = true };
                        await WriteMessageAsync(stream, ack, ct);
                        break;
                    }
                    case ManifestRequestType.NotifyNewOperation when !string.IsNullOrWhiteSpace(request.TargetUserId):
                    {
                        _logger.Debug("Received NotifyNewOperation from {0} for user {1} stream {2} seq {3}",
                            remoteEndpoint, request.TargetUserId, request.StreamType, request.StartSequenceNumber);

                        NotificationReceived?.Invoke(this, new NotifyNewOperationEventArgs(request.TargetUserId, request.StreamType, request.StartSequenceNumber, remoteEndpoint, request.AnnouncingPeer));

                        var ack = new ManifestResponse { Acknowledged = true };
                        await WriteMessageAsync(stream, ack, ct);
                        break;
                    }
                    case ManifestRequestType.GetPeers:
                    {
                        var peers = _peersProvider?.Invoke()
                            .Take(SecurityLimits.MaxPeersPerExchange)
                            .ToList() ?? [];
                        _logger.Info("Serving {0} peers to {1} (PEX)", peers.Count, remoteEndpoint);
                        var response = new ManifestResponse { Peers = peers };
                        await WriteMessageAsync(stream, response, ct);
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

                        _logger.Info("Rendezvous request from {0} (Target: {1}) -> Success: {2}",
                            remoteEndpoint, request.Rendezvous.TargetUserId, rendezvous.Success);
                        var response = new ManifestResponse { Rendezvous = rendezvous, Acknowledged = rendezvous.Success };
                        await WriteMessageAsync(stream, response, ct);
                        break;
                    }
                    case ManifestRequestType.RequestContent when !string.IsNullOrWhiteSpace(request.ContentHash):
                    {
                        var contentBytes = _contentProvider?.Invoke(request.ContentHash);
                        _logger.Info("Content request from {0} for hash {1}. Found: {2}",
                            remoteEndpoint, request.ContentHash, contentBytes != null);
                        var response = new ManifestResponse
                        {
                            Acknowledged = contentBytes != null && contentBytes.Length > 0,
                            ContentLength = contentBytes?.Length ?? 0
                        };
                        await WriteMessageAsync(stream, response, ct);
                        if (contentBytes != null && contentBytes.Length > 0)
                        {
                            await stream.WriteAsync(contentBytes, ct);
                            await stream.FlushAsync(ct);
                        }
                        break;
                    }
                    default:
                    {
                        var response = new ManifestResponse { Acknowledged = false };
                        await WriteMessageAsync(stream, response, ct);
                        break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                _logger.Debug("Client {0} disconnected before sending a complete message (expected for TCP probes).", remoteEndpoint);
            }
            catch (IOException ex)
            {
                _logger.Debug("IO error with client {0}: {1}", remoteEndpoint, ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Connection with {0} was canceled.", remoteEndpoint);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error handling client {0}", remoteEndpoint);
            }
        }
    }

    internal static Task WriteMessageAsync(Stream stream, ManifestRequest request, CancellationToken ct)
    {
        var body = ManifestSerializer.SerializeRequest(request);
        return WriteBytesAsync(stream, body, ct);
    }

    internal static Task WriteMessageAsync(Stream stream, ManifestResponse response, CancellationToken ct)
    {
        var body = ManifestSerializer.SerializeResponse(response);
        return WriteBytesAsync(stream, body, ct);
    }

    private static async Task WriteBytesAsync(Stream stream, byte[] body, CancellationToken ct)
    {
        var lengthBytes = BitConverter.GetBytes(body.Length);
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    internal static async Task<(byte[] Bytes, bool IsJson)> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var lengthBytes = new byte[4];
        var totalRead = 0;
        while (totalRead < 4)
        {
            var read = await stream.ReadAsync(lengthBytes.AsMemory(totalRead, 4 - totalRead), ct);
            if (read == 0) throw new EndOfStreamException("End of stream reached while reading length.");
            totalRead += read;
        }
        var length = BitConverter.ToInt32(lengthBytes, 0);

        if (length < 0 || length > SecurityLimits.MaxMessageBytes)
            throw new InvalidDataException($"Rejected message: length {length} exceeds limit.");

        if (length == 0) return ([], false);

        var body = new byte[length];
        totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(totalRead, length - totalRead), ct);
            if (read == 0) throw new EndOfStreamException("End of stream reached while reading body.");
            totalRead += read;
        }

        var isJson = body.Length > 0 && body[0] == (byte)'{';
        return (body, isJson);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}

public class NotifyNewOperationEventArgs(string targetUserId, ManifestStreamType streamType, int startSequenceNumber, string peerAddress, PeerInfo? announcingPeer) : EventArgs
{
    public string TargetUserId { get; } = targetUserId;
    public ManifestStreamType StreamType { get; } = streamType;
    public int StartSequenceNumber { get; } = startSequenceNumber;
    public string PeerAddress { get; } = peerAddress;
    public PeerInfo? AnnouncingPeer { get; } = announcingPeer;
}

public class ManifestReceivedEventArgs(Manifest manifest, string peerAddress, PeerInfo? announcingPeer, bool isRelay) : EventArgs
{
    public Manifest Manifest { get; } = manifest;
    public string PeerAddress { get; } = peerAddress;
    public PeerInfo? AnnouncingPeer { get; } = announcingPeer;
    public bool IsRelay { get; } = isRelay;
}
