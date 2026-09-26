using System.Collections.Concurrent;
using System.Net;
using LiteNetLib;
using MeshWave.Common.Core;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// One UDP socket per node, used for NAT hole punching and for reliable sessions over punched paths
/// (LiteNetLib reliable-ordered channel).
///
/// Introducer role: when two peers send a NAT introduce request with the same token (issued by
/// <see cref="CreateIntroductionToken"/>), the host observes each request's public source endpoint (like STUN) and
/// sends both peers each other's public and private endpoints.
///
/// Punching role: after <see cref="ExpectIntroduction"/>, the host keeps sending introduce requests to the introducer
/// until the introduction arrives, then both peers punch and connect at the same time.
/// Only connections carrying an expected token are accepted.
/// </summary>
public sealed class UdpSessionHost : IDisposable
{
    private const int IntroduceRequestIntervalMs = 400;

    private readonly Logger _logger;
    private readonly EventBasedNetListener _listener = new();
    private readonly EventBasedNatPunchListener _natListener = new();
    private readonly NetManager _net;

    // Introducer state: token -> endpoints of the (up to two) peers that asked for it.
    private readonly ConcurrentDictionary<string, IntroductionSlot> _introductions = new(StringComparer.Ordinal);

    // Punching state: token -> expected introduction.
    private readonly ConcurrentDictionary<string, ExpectedIntroduction> _expected = new(StringComparer.Ordinal);

    public UdpSessionHost(Logger? logger = null)
    {
        _logger = logger ?? LogManager.GetCurrentClassLogger();
        _net = new NetManager(_listener)
        {
            NatPunchEnabled = true,
            UnsyncedEvents = true,
            DisconnectTimeout = SecurityLimits.SessionIdleTimeoutSeconds * 1000,
            PingInterval = 1000
        };

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _natListener.NatIntroductionRequest += OnNatIntroductionRequest;
        _natListener.NatIntroductionSuccess += OnNatIntroductionSuccess;
    }

    /// <summary>The bound UDP port, or 0 when not running.</summary>
    public int Port => IsRunning ? _net.LocalPort : 0;

    public bool IsRunning { get; private set; }

    /// <summary>Raised when a punched connection is established. The handler owns the transport.</summary>
    public event Action<UdpFrameTransport>? TransportConnected;

    /// <summary>Binds the UDP socket, preferring <paramref name="preferredPort"/> (0 = any free port).</summary>
    public bool Start(int preferredPort)
    {
        if (IsRunning) return true;

        _net.NatPunchModule.Init(_natListener);
        _net.NatPunchModule.UnsyncedEvents = true;

        IsRunning = (preferredPort > 0 && _net.Start(preferredPort)) || _net.Start(0);
        if (IsRunning)
            _logger.Debug("UDP session host listening on port {0}", _net.LocalPort);
        else
            _logger.Warn("UDP session host could not bind a port; hole punching is disabled.");
        return IsRunning;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        foreach (var expected in _expected.Values) expected.Cancel();
        _expected.Clear();
        _introductions.Clear();
        try { _net.Stop(); } catch { }
    }

    // ── Introducer role ───────────────────────────────────────────────────

    /// <summary>Issues a single-use token that lets exactly two peers be introduced to each other.</summary>
    public string CreateIntroductionToken()
    {
        PruneIntroductions();
        var token = Guid.NewGuid().ToString("N");
        _introductions[token] = new IntroductionSlot { ExpiresUtc = DateTime.UtcNow.AddSeconds(SecurityLimits.IntroductionTimeoutSeconds) };
        return token;
    }

    private void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        if (!_introductions.TryGetValue(token, out var slot) || slot.ExpiresUtc < DateTime.UtcNow)
            return;

        (IPEndPoint Internal, IPEndPoint External)? first, second;
        lock (slot)
        {
            // Peers resend their request until introduced; the same public endpoint counts once.
            var known = slot.Endpoints.FindIndex(e => e.External.Equals(remoteEndPoint));
            if (known >= 0)
                slot.Endpoints[known] = (localEndPoint, remoteEndPoint);
            else if (slot.Endpoints.Count < 2)
                slot.Endpoints.Add((localEndPoint, remoteEndPoint));
            else
                return;

            if (slot.Endpoints.Count < 2) return;
            first = slot.Endpoints[0];
            second = slot.Endpoints[1];
        }

        _logger.Debug("Introducing {0} and {1} (token {2})", first.Value.External, second.Value.External, token[..8]);
        _net.NatPunchModule.NatIntroduce(first.Value.Internal, first.Value.External, second.Value.Internal, second.Value.External, token);
    }

    private void PruneIntroductions()
    {
        var now = DateTime.UtcNow;
        foreach (var expired in _introductions.Where(kv => kv.Value.ExpiresUtc < now).Select(kv => kv.Key).ToList())
            _introductions.TryRemove(expired, out _);
    }

    // ── Punching role ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts asking <paramref name="introducer"/> for the introduction identified by <paramref name="token"/>, and
    /// accepts a connection carrying that token. <paramref name="remoteUserId"/> is only used for diagnostics.
    /// </summary>
    public void ExpectIntroduction(string token, string remoteUserId, IPEndPoint introducer)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(token) || token.Length > 64) return;
        if (_expected.Count >= 32) return;

        var expected = new ExpectedIntroduction(token, remoteUserId, SecurityLimits.IntroductionTimeoutSeconds);
        if (!_expected.TryAdd(token, expected)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!expected.Token.IsCancellationRequested && !expected.Punched)
                {
                    _net.NatPunchModule.SendNatIntroduceRequest(introducer, token);
                    await Task.Delay(IntroduceRequestIntervalMs, expected.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Debug("Introduce request to {0} failed: {1}", introducer, ex.Message);
            }

            // Keep the token acceptable a little longer than the punch window so a late connect still succeeds.
            try { await Task.Delay(TimeSpan.FromSeconds(SecurityLimits.IntroductionTimeoutSeconds)); } catch { }
            _expected.TryRemove(token, out _);
        });
    }

    private void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        if (!_expected.TryGetValue(token, out var expected)) return;

        // Both the private and the public endpoint may answer; connect only once per introduction.
        if (!expected.TryMarkPunched()) return;

        _logger.Debug("Punched through to {0} at {1} ({2}); connecting", expected.RemoteUserId, targetEndPoint, type);
        _net.Connect(targetEndPoint, token);
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        string? key = null;
        try { key = request.Data.GetString(64); } catch { }

        if (key != null && _expected.ContainsKey(key))
            request.Accept();
        else
            request.Reject();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        var transport = new UdpFrameTransport(peer);
        peer.Tag = transport;
        _logger.Debug("UDP connection established with {0}", transport.RemoteAddress);
        TransportConnected?.Invoke(transport);
    }

    private static void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        (peer.Tag as UdpFrameTransport)?.OnDisconnected();
    }

    private static void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        var bytes = reader.GetRemainingBytes();
        reader.Recycle();
        (peer.Tag as UdpFrameTransport)?.OnFrame(bytes);
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class IntroductionSlot
    {
        public DateTime ExpiresUtc { get; init; }
        public List<(IPEndPoint Internal, IPEndPoint External)> Endpoints { get; } = [];
    }

    private sealed class ExpectedIntroduction
    {
        private readonly CancellationTokenSource _cts;
        private int _punched;

        public ExpectedIntroduction(string token, string remoteUserId, int timeoutSeconds)
        {
            TokenValue = token;
            RemoteUserId = remoteUserId;
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        }

        public string TokenValue { get; }
        public string RemoteUserId { get; }
        public CancellationToken Token => _cts.Token;
        public bool Punched => Volatile.Read(ref _punched) != 0;

        public bool TryMarkPunched()
        {
            return Interlocked.Exchange(ref _punched, 1) == 0;
        }

        public void Cancel()
        {
            _cts.Cancel();
        }
    }
}

/// <summary>
/// Frames over a LiteNetLib reliable-ordered UDP connection. LiteNetLib fragments large frames.
/// </summary>
public sealed class UdpFrameTransport : IFrameTransport
{
    private readonly NetPeer _peer;
    private int _closed;

    public UdpFrameTransport(NetPeer peer)
    {
        _peer = peer;
        RemoteAddress = peer.Address.IsIPv4MappedToIPv6 ? peer.Address.MapToIPv4().ToString() : peer.Address.ToString();
    }

    public string RemoteAddress { get; }
    public string Kind => "udp";

    public event Action<byte[]>? FrameReceived;
    public event Action? Closed;

    public void Start()
    {
    }

    public Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closed) != 0)
            throw new IOException("UDP session is closed.");

        _peer.Send(frame, DeliveryMethod.ReliableOrdered);
        return Task.CompletedTask;
    }

    internal void OnFrame(byte[] frame)
    {
        FrameReceived?.Invoke(frame);
    }

    internal void OnDisconnected()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        Closed?.Invoke();
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _closed) != 0) return;
        try { _peer.Disconnect(); } catch { }
        OnDisconnected();
    }
}
