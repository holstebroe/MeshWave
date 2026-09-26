using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MeshWave.Common.Core;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// A reliable, ordered, message-oriented link to one remote node (a TCP connection or a hole-punched UDP connection).
/// </summary>
public interface IFrameTransport : IDisposable
{
    /// <summary>The remote IP address as observed on this link.</summary>
    string RemoteAddress { get; }

    /// <summary>"tcp" or "udp", for diagnostics.</summary>
    string Kind { get; }

    event Action<byte[]>? FrameReceived;
    event Action? Closed;

    void Start();
    Task SendAsync(byte[] frame, CancellationToken cancellationToken);
}

/// <summary>
/// Length-prefixed frames over a TCP stream, using the same 4-byte little-endian length prefix as one-shot messages.
/// </summary>
public sealed class TcpFrameTransport : IFrameTransport
{
    private const int MaxFrameBytes = SecurityLimits.MaxMessageBytes + 64;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    public TcpFrameTransport(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        // Sessions are long-lived; idle detection is done by the session keepalive, not by socket timeouts.
        _client.ReceiveTimeout = 0;
        _client.SendTimeout = SecurityLimits.ReadTimeoutMs;
        _stream = client.GetStream();

        var remote = client.Client.RemoteEndPoint as IPEndPoint;
        var address = remote?.Address;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        RemoteAddress = address?.ToString() ?? string.Empty;
    }

    public string RemoteAddress { get; }
    public string Kind => "tcp";

    public event Action<byte[]>? FrameReceived;
    public event Action? Closed;

    public void Start()
    {
        _ = Task.Run(ReadLoopAsync);
    }

    public async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        if (frame.Length > MaxFrameBytes)
            throw new InvalidDataException($"Frame of {frame.Length} bytes exceeds the limit.");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, frame.Length);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(header, cancellationToken);
            await _stream.WriteAsync(frame, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        catch
        {
            Close();
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var header = new byte[4];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await _stream.ReadExactlyAsync(header, _cts.Token);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0 || length > MaxFrameBytes)
                    throw new InvalidDataException($"Rejected frame: length {length} exceeds limit.");

                var frame = new byte[length];
                await _stream.ReadExactlyAsync(frame, _cts.Token);
                FrameReceived?.Invoke(frame);
            }
        }
        catch
        {
            // Connection closed or corrupted; fall through to Close.
        }
        finally
        {
            Close();
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _cts.Cancel();
        try { _client.Dispose(); } catch { }
        Closed?.Invoke();
    }

    public void Dispose()
    {
        Close();
    }
}

/// <summary>
/// A persistent, bidirectional connection to one neighbour. Requests are multiplexed in both directions:
/// each frame carries a kind and a request ID, so either side can push or request at any time, and several
/// requests can be in flight at once. A keepalive every <see cref="SecurityLimits.SessionKeepaliveSeconds"/>
/// holds NAT mappings open and detects dead links.
/// </summary>
public sealed class PeerSession : IDisposable
{
    private enum FrameKind : byte
    {
        Request = 1,
        Response = 2,
        Ping = 3,
        Pong = 4
    }

    private const int HeaderBytes = 5;
    private const int MaxConcurrentInboundRequests = 16;

    private readonly IFrameTransport _transport;
    private readonly Func<ManifestRequest, PeerSession, CancellationToken, Task<ManifestResponse>> _handler;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ManifestResponse>> _pending = new();
    private readonly SemaphoreSlim _inboundLimiter = new(MaxConcurrentInboundRequests, MaxConcurrentInboundRequests);
    private readonly CancellationTokenSource _cts = new();
    private int _nextRequestId;
    private int _closed;
    private long _lastReceivedTicks = DateTime.UtcNow.Ticks;
    private long _lastActivityReportTicks;

    public PeerSession(
        IFrameTransport transport,
        bool isInitiator,
        string? dialEndpoint,
        Func<ManifestRequest, PeerSession, CancellationToken, Task<ManifestResponse>> handler,
        Logger logger)
    {
        _transport = transport;
        _handler = handler;
        _logger = logger;
        IsInitiator = isInitiator;
        DialEndpoint = dialEndpoint;
        LocalNonce = BinaryPrimitives.ReadInt64LittleEndian(RandomNumberGenerator.GetBytes(8));

        _transport.FrameReceived += OnFrameReceived;
        _transport.Closed += Close;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>True if this side opened the connection.</summary>
    public bool IsInitiator { get; }

    /// <summary>The "host:port" this side dialled, for sessions we opened over TCP; otherwise null.</summary>
    public string? DialEndpoint { get; }

    public string RemoteAddress => _transport.RemoteAddress;
    public string TransportKind => _transport.Kind;

    /// <summary>Random value the remote must sign to prove its identity (see <see cref="SessionHello"/>).</summary>
    public long LocalNonce { get; }

    /// <summary>The remote's nonce, once its hello was seen.</summary>
    public long? RemoteNonce { get; internal set; }

    /// <summary>The remote's own (verified) peer record, once it has proven ownership of its key. Null for anonymous nodes.</summary>
    public PeerInfo? RemotePeer { get; internal set; }

    public string? RemoteUserId => RemotePeer?.UserId;
    public bool IsAuthenticated => RemotePeer != null;

    /// <summary>True once the remote answered our hello (authenticated or anonymous).</summary>
    public bool HandshakeCompleted { get; internal set; }

    public bool RemoteIsIntroducer { get; internal set; }
    public int RemoteUdpPort { get; internal set; }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public DateTime LastReceivedUtc => new(Interlocked.Read(ref _lastReceivedTicks), DateTimeKind.Utc);
    public DateTime OpenedUtc { get; } = DateTime.UtcNow;

    /// <summary>
    /// Deterministic ordering of duplicate sessions between the same two nodes. Both sides compute the same value
    /// from the two nonces, so both keep the same session when duplicates are pruned.
    /// </summary>
    public ulong Rank
    {
        get
        {
            if (RemoteNonce is not { } remote) return 0;
            var (low, high) = LocalNonce < remote ? (LocalNonce, remote) : (remote, LocalNonce);
            Span<byte> buffer = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, low);
            BinaryPrimitives.WriteInt64LittleEndian(buffer[8..], high);
            return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(buffer));
        }
    }

    public event Action<PeerSession>? Closed;

    /// <summary>Raised on incoming traffic, at most every 10 seconds (used to refresh routing-table liveness).</summary>
    public event Action<PeerSession>? Activity;

    public void Start()
    {
        _transport.Start();
        _ = Task.Run(KeepaliveLoopAsync);
    }

    /// <summary>
    /// Sends a request and waits for the response. Returns null if the session closes or the request times out.
    /// </summary>
    public async Task<ManifestResponse?> RequestAsync(ManifestRequest request, int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (IsClosed) return null;

        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<ManifestResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            cts.CancelAfter(timeoutMs);

            await SendFrameAsync(FrameKind.Request, id, ManifestSerializer.SerializeRequest(request), cts.Token);
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidDataException)
        {
            if (!cancellationToken.IsCancellationRequested)
                _logger.Debug("Session {0} request {1} ({2}) failed: {3}", Id, id, request.Type, ex.Message);
            return null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendFrameAsync(FrameKind kind, int id, byte[] body, CancellationToken cancellationToken)
    {
        var frame = new byte[HeaderBytes + body.Length];
        frame[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(1), id);
        body.CopyTo(frame, HeaderBytes);
        await _transport.SendAsync(frame, cancellationToken);
    }

    private void OnFrameReceived(byte[] frame)
    {
        if (frame.Length < HeaderBytes) return;

        var now = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _lastReceivedTicks, now);
        if (now - Interlocked.Read(ref _lastActivityReportTicks) > TimeSpan.TicksPerSecond * 10)
        {
            Interlocked.Exchange(ref _lastActivityReportTicks, now);
            Activity?.Invoke(this);
        }

        var kind = (FrameKind)frame[0];
        var id = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(1));
        var body = frame.AsSpan(HeaderBytes).ToArray();

        switch (kind)
        {
            case FrameKind.Ping:
                _ = SendQuietlyAsync(FrameKind.Pong, id, []);
                break;
            case FrameKind.Pong:
                break;
            case FrameKind.Response:
                if (_pending.TryRemove(id, out var tcs))
                    try
                    {
                        tcs.TrySetResult(ManifestSerializer.DeserializeResponse(body));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                break;
            case FrameKind.Request:
                _ = Task.Run(() => HandleRequestAsync(id, body));
                break;
        }
    }

    private async Task HandleRequestAsync(int id, byte[] body)
    {
        try
        {
            if (!await _inboundLimiter.WaitAsync(TimeSpan.FromSeconds(30), _cts.Token))
                return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            ManifestResponse response;
            try
            {
                var request = ManifestSerializer.DeserializeRequest(body);
                response = await _handler(request, this, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Debug("Session {0} failed to handle request {1}: {2}", Id, id, ex.Message);
                response = new ManifestResponse { Acknowledged = false };
            }

            await SendQuietlyAsync(FrameKind.Response, id, ManifestSerializer.SerializeResponse(response));
        }
        finally
        {
            _inboundLimiter.Release();
        }
    }

    private async Task SendQuietlyAsync(FrameKind kind, int id, byte[] body)
    {
        try
        {
            await SendFrameAsync(kind, id, body, _cts.Token);
        }
        catch
        {
            // The transport closes itself on write failures.
        }
    }

    private async Task KeepaliveLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(SecurityLimits.SessionKeepaliveSeconds), _cts.Token);

                if (DateTime.UtcNow - LastReceivedUtc > TimeSpan.FromSeconds(SecurityLimits.SessionIdleTimeoutSeconds))
                {
                    _logger.Debug("Session {0} with {1} timed out.", Id, RemoteUserId ?? RemoteAddress);
                    Close();
                    return;
                }

                await SendQuietlyAsync(FrameKind.Ping, 0, []);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;

        _cts.Cancel();
        _transport.Dispose();
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();
        Closed?.Invoke(this);
    }

    public void Dispose()
    {
        Close();
    }

    public override string ToString()
    {
        return $"{TransportKind}:{Id}:{RemoteUserId ?? DialEndpoint ?? RemoteAddress}";
    }
}
