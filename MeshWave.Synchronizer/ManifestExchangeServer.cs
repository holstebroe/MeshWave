using MeshWave.Common.Core;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using NLog;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Logger = NLog.Logger;

namespace MeshWave.Synchronizer;

/// <summary>
/// Serves manifest exchange requests: GetManifest, PushManifest, GetPeers (PEX), Announce, RequestContent and Ping.
/// Requests arrive either as one-shot TCP connections (one request, one response) or over persistent
/// <see cref="PeerSession"/>s; both go through <see cref="HandleRequestAsync"/>.
/// A one-shot <see cref="ManifestRequestType.OpenSession"/> request upgrades the TCP connection to a session.
/// All message sizes are enforced against SecurityLimits.
/// </summary>
public class ManifestExchangeServer : IDisposable
{
    private readonly Logger _logger;
    public const int DefaultPort = 39877;

    /// <summary>Largest content slice sent inline in a session response (sessions carry content in the message body).</summary>
    internal const int MaxSessionContentSliceBytes = 1024 * 1024;

    private static readonly TimeSpan DialBackCacheDuration = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, (bool Reachable, DateTime CheckedUtc)> _dialBackCache = new(StringComparer.Ordinal);

    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    private Func<ManifestStreamType, Manifest?>? _localManifestProvider;
    private Func<IReadOnlyList<PeerInfo>>? _peersProvider;
    private Func<string, byte[]?>? _contentProvider;
    private Func<PeerInfo?>? _selfInfoProvider;

    public ManifestExchangeServer(int port = DefaultPort, Logger? logger = null)
    {
        _port = port;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public event EventHandler<ManifestReceivedEventArgs>? ManifestReceived;

    /// <summary>Raised when a peer registers itself via <see cref="ManifestRequestType.Announce"/>.</summary>
    public event EventHandler<PeerAnnouncedEventArgs>? PeerAnnounced;

    /// <summary>
    /// Takes ownership of a TCP connection whose peer asked for a persistent session. When not set,
    /// <see cref="ManifestRequestType.OpenSession"/> requests are refused.
    /// </summary>
    public Action<TcpClient>? SessionUpgradeHandler { get; set; }

    /// <summary>Whether Announce requests with a port are verified by connecting back to it. Disabled for tests only.</summary>
    public bool DialBackEnabled { get; set; } = true;

    public bool IsListening => _listener != null;

    /// <summary>
    /// Sets the data sources used to answer requests. Needed even without a listener, because requests also arrive over sessions.
    /// </summary>
    /// <param name="localManifestProvider">Returns this peer's current manifest on demand for a given stream.</param>
    /// <param name="peersProvider">Returns known peers for PEX responses. May be null to disable PEX serving.</param>
    /// <param name="contentProvider">Returns content bytes by hash. May be null to serve no content.</param>
    /// <param name="selfInfoProvider">Returns this node's own peer record, reported in Announce responses. Null for a standalone bootstrap.</param>
    public void Configure(
        Func<ManifestStreamType, Manifest?> localManifestProvider,
        Func<IReadOnlyList<PeerInfo>>? peersProvider = null,
        Func<string, byte[]?>? contentProvider = null,
        Func<PeerInfo?>? selfInfoProvider = null)
    {
        _localManifestProvider = localManifestProvider;
        _peersProvider = peersProvider;
        _contentProvider = contentProvider;
        _selfInfoProvider = selfInfoProvider;
    }

    /// <summary>
    /// Configures the server and starts listening for TCP connections.
    /// </summary>
    public Task StartAsync(
        Func<ManifestStreamType, Manifest?> localManifestProvider,
        Func<IReadOnlyList<PeerInfo>>? peersProvider = null,
        Func<string, byte[]?>? contentProvider = null,
        Func<PeerInfo?>? selfInfoProvider = null,
        CancellationToken cancellationToken = default)
    {
        Configure(localManifestProvider, peersProvider, contentProvider, selfInfoProvider);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        _serverTask = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
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

        var keepOpen = false;
        try
        {
            client.ReceiveTimeout = SecurityLimits.ReadTimeoutMs;
            client.SendTimeout = SecurityLimits.ReadTimeoutMs;

            var stream = client.GetStream();
            var (bytes, isJson) = await ReadMessageAsync(stream, ct);

            var request = isJson
                ? JsonSerializer.Deserialize<ManifestRequest>(Encoding.UTF8.GetString(bytes))
                : ManifestSerializer.DeserializeRequest(bytes);

            if (request == null)
            {
                _logger.Warn("Received empty or invalid request from {0}", remoteEndpoint);
                return;
            }

            _logger.Debug("Received {0} request from {1} (format={2})", request.Type, remoteEndpoint, isJson ? "JSON" : "Protobuf");
            var remoteAddress = ObservedAddressOf(client);

            if (request.Type == ManifestRequestType.OpenSession)
            {
                var upgrade = SessionUpgradeHandler;
                await WriteMessageAsync(stream, new ManifestResponse { Acknowledged = upgrade != null, ObservedAddress = remoteAddress }, ct);
                if (upgrade != null)
                {
                    keepOpen = true;
                    upgrade(client);
                }
                return;
            }

            var (response, content) = await HandleRequestAsync(request, remoteAddress, inlineContent: false, ct);
            await WriteMessageAsync(stream, response, ct);
            if (!content.IsEmpty)
            {
                await stream.WriteAsync(content, ct);
                await stream.FlushAsync(ct);
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
        finally
        {
            if (!keepOpen) client.Dispose();
        }
    }

    /// <summary>
    /// Answers one request. <paramref name="remoteAddress"/> is the requester's observed IP.
    /// With <paramref name="inlineContent"/> (sessions) content bytes are returned in <see cref="ManifestResponse.ContentBytes"/>,
    /// capped at <see cref="MaxSessionContentSliceBytes"/>; otherwise they are returned separately, to be written after the response.
    /// </summary>
    public async Task<(ManifestResponse Response, ReadOnlyMemory<byte> Content)> HandleRequestAsync(
        ManifestRequest request, string remoteAddress, bool inlineContent, CancellationToken ct)
    {
        switch (request.Type)
        {
            case ManifestRequestType.GetManifest:
                return (new ManifestResponse { Manifest = BuildManifestResponse(request, remoteAddress) }, ReadOnlyMemory<byte>.Empty);

            case ManifestRequestType.PushManifest when request.Manifest != null:
                {
                    var opCount = request.Manifest.Operations.Count;
                    if (opCount <= SecurityLimits.MaxManifestOperations)
                    {
                        _logger.Info("Received manifest push from {0} (User: {1}, Ops: {2})", remoteAddress, request.Manifest.UserId, opCount);
                        ManifestReceived?.Invoke(this, new ManifestReceivedEventArgs(request.Manifest, remoteAddress, request.AnnouncingPeer));
                    }
                    else
                    {
                        _logger.Warn("Rejected push from {0}: too many operations ({1})", remoteAddress, opCount);
                    }
                    return (new ManifestResponse { Acknowledged = true }, ReadOnlyMemory<byte>.Empty);
                }

            case ManifestRequestType.Announce when request.AnnouncingPeer != null:
                return (await HandleAnnounceAsync(request.AnnouncingPeer, remoteAddress, ct), ReadOnlyMemory<byte>.Empty);

            case ManifestRequestType.GetPeers:
                {
                    var peers = _peersProvider?.Invoke()
                        .Take(SecurityLimits.MaxPeersPerExchange)
                        .ToList() ?? [];
                    _logger.Info("Serving {0} peers to {1} (PEX)", peers.Count, remoteAddress);
                    return (new ManifestResponse { Peers = peers }, ReadOnlyMemory<byte>.Empty);
                }

            case ManifestRequestType.Ping:
                return (new ManifestResponse { Acknowledged = true, ObservedAddress = remoteAddress }, ReadOnlyMemory<byte>.Empty);

            case ManifestRequestType.RequestContent when !string.IsNullOrWhiteSpace(request.ContentHash):
                {
                    var contentBytes = _contentProvider?.Invoke(request.ContentHash);
                    _logger.Info("Content request from {0} for hash {1}. Found: {2}", remoteAddress, request.ContentHash, contentBytes != null);

                    var found = contentBytes != null && contentBytes.Length > 0;
                    var (sliceOffset, sliceLength) = found
                        ? ResolveContentSlice(contentBytes!.LongLength, request.ChunkOffset, request.ChunkLength)
                        : (0L, 0L);
                    if (inlineContent)
                        sliceLength = Math.Min(sliceLength, MaxSessionContentSliceBytes);

                    var slice = sliceLength > 0 ? contentBytes.AsMemory((int)sliceOffset, (int)sliceLength) : ReadOnlyMemory<byte>.Empty;
                    var response = new ManifestResponse
                    {
                        Acknowledged = found,
                        ContentLength = sliceLength,
                        TotalContentLength = found ? contentBytes!.LongLength : null,
                        ContentBytes = inlineContent && sliceLength > 0 ? slice.ToArray() : null
                    };
                    return (response, inlineContent ? ReadOnlyMemory<byte>.Empty : slice);
                }

            default:
                return (new ManifestResponse { Acknowledged = false }, ReadOnlyMemory<byte>.Empty);
        }
    }

    private Manifest? BuildManifestResponse(ManifestRequest request, string remoteAddress)
    {
        var originalManifest = _localManifestProvider?.Invoke(request.StreamType);
        if (originalManifest == null)
        {
            _logger.Debug("No {0} manifest to serve to {1}", request.StreamType, remoteAddress);
            return null;
        }

        _logger.Info("Serving manifest for {0} to {1} (delta={2}, ops={3})",
            originalManifest.UserId, remoteAddress, request.StartSequenceNumber > 0, originalManifest.Operations.Count);

        lock (originalManifest)
        {
            var snapshot = originalManifest.Snapshot;
            if (request.StartSequenceNumber > (snapshot?.LastSequenceNumber ?? -1)) snapshot = null;

            var filteredOps = originalManifest.Operations
                .Where(op => op.SequenceNumber >= request.StartSequenceNumber &&
                            (request.EndSequenceNumber == null || op.SequenceNumber <= request.EndSequenceNumber))
                .ToList();

            return new Manifest
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

    private async Task<ManifestResponse> HandleAnnounceAsync(PeerInfo announced, string observedAddress, CancellationToken ct)
    {
        var announcedPort = announced.Port is > 0 and < 65536 ? announced.Port : 0;

        // Only a port we can actually connect back to is registered; otherwise the peer is outbound-only.
        bool? dialBack = null;
        if (announcedPort > 0 && DialBackEnabled && !string.IsNullOrWhiteSpace(observedAddress))
        {
            dialBack = await DialBackAsync(observedAddress, announcedPort, ct);
            if (dialBack == false) announcedPort = 0;
        }

        var peer = new PeerInfo
        {
            UserId = announced.UserId,
            DisplayName = SecurityLimits.Truncate(announced.DisplayName, SecurityLimits.MaxDisplayNameLength),
            // Always trust the observed source address over a self-reported one.
            Address = string.IsNullOrWhiteSpace(observedAddress) ? announced.Address : observedAddress,
            Port = announcedPort,
            PublicKeyPem = announced.PublicKeyPem,
            LastSeen = DateTime.UtcNow,
            Capabilities = announced.Capabilities.Take(8).ToList()
        };

        // The owner's signature stays attached only if it still describes the registered address and port.
        if (announced.Address == peer.Address && announced.Port == peer.Port && PeerRecords.IsValidlySigned(announced))
        {
            peer.SignedAtUtc = announced.SignedAtUtc;
            peer.Signature = announced.Signature;
        }

        _logger.Info("Peer {0} announced from {1} (port {2}, dial-back {3})", peer.UserId, observedAddress, peer.Port,
            dialBack switch { true => "ok", false => "failed", null => "skipped" });
        PeerAnnounced?.Invoke(this, new PeerAnnouncedEventArgs(peer));

        var self = _selfInfoProvider?.Invoke();
        return new ManifestResponse
        {
            Acknowledged = true,
            Peers = self != null ? [self] : [],
            ObservedAddress = observedAddress,
            DialBackSucceeded = dialBack
        };
    }

    /// <summary>
    /// Checks that a MeshWave node answers on <paramref name="address"/>:<paramref name="port"/>.
    /// Only ever dials the requester's own observed address, so it cannot be used to probe third parties.
    /// </summary>
    private async Task<bool> DialBackAsync(string address, int port, CancellationToken ct)
    {
        var key = $"{address}:{port}";
        if (_dialBackCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.CheckedUtc < DialBackCacheDuration)
            return cached.Reachable;

        var reachable = false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(SecurityLimits.DialBackTimeoutMs);
            using var probe = new TcpClient();
            await probe.ConnectAsync(address, port, cts.Token);
            var stream = probe.GetStream();
            await WriteMessageAsync(stream, new ManifestRequest { Type = ManifestRequestType.Ping }, cts.Token);
            var (bytes, _) = await ReadMessageAsync(stream, cts.Token);
            reachable = ManifestSerializer.DeserializeResponse(bytes).Acknowledged;
        }
        catch (Exception ex)
        {
            _logger.Debug("Dial-back to {0} failed: {1}", key, ex.Message);
        }

        if (_dialBackCache.Count > 1000) _dialBackCache.Clear();
        _dialBackCache[key] = (reachable, DateTime.UtcNow);
        return reachable;
    }

    private static string ObservedAddressOf(TcpClient client)
    {
        var address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        return address?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Resolves the byte range to send for a content request.
    /// Without a chunk offset the whole content is sent. A chunk request is clamped to the content bounds;
    /// a zero-length chunk (or an offset past the end) sends no bytes, which lets clients probe the total length.
    /// </summary>
    internal static (long Offset, long Length) ResolveContentSlice(long totalLength, long? chunkOffset, long? chunkLength)
    {
        if (!chunkOffset.HasValue)
            return (0, totalLength);

        var offset = Math.Clamp(chunkOffset.Value, 0, totalLength);
        var remaining = totalLength - offset;
        var length = chunkLength.HasValue ? Math.Clamp(chunkLength.Value, 0, remaining) : remaining;
        return (offset, length);
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

public class PeerAnnouncedEventArgs(PeerInfo peer) : EventArgs
{
    /// <summary>
    /// The announcing peer, with <see cref="PeerInfo.Address"/> set to the observed source address and
    /// <see cref="PeerInfo.Port"/> set to 0 if the dial-back failed.
    /// </summary>
    public PeerInfo Peer { get; } = peer;
}

public class ManifestReceivedEventArgs(Manifest manifest, string peerAddress, PeerInfo? announcingPeer) : EventArgs
{
    public Manifest Manifest { get; } = manifest;
    public string PeerAddress { get; } = peerAddress;
    public PeerInfo? AnnouncingPeer { get; } = announcingPeer;
}
