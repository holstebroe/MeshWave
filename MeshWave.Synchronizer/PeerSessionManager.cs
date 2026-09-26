using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MeshWave.Common.Core;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// Owns this node's persistent <see cref="PeerSession"/>s: inbound TCP sessions (upgraded by
/// <see cref="ManifestExchangeServer"/>), outbound TCP sessions it dials, and hole-punched UDP sessions.
///
/// Every session starts with a <see cref="ManifestRequestType.Hello"/> exchange in both directions. Each side's hello
/// carries a fresh nonce, and the answer signs it, so a session is only attributed to a UserId after that user proved
/// ownership of the key. Nodes without an identity (a standalone bootstrap) stay anonymous.
///
/// Introducer role: a node forwards <see cref="ManifestRequestType.RequestIntroduction"/> from one authenticated
/// session to another as an <see cref="ManifestRequestType.IntroductionOffer"/>, and pairs the two peers' NAT
/// introduce requests on its UDP socket (<see cref="UdpSessionHost"/>).
/// </summary>
public sealed class PeerSessionManager : IDisposable
{
    private const int HandshakeRequestTimeoutMs = 10_000;
    private const int IntroductionRequestTimeoutMs = 5_000;
    private const int MaxIntroductionsPerMinutePerRequester = 10;

    private readonly Logger _logger;
    private readonly UdpSessionHost _udp;
    private readonly ConcurrentDictionary<string, PeerSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<PeerSession?>> _pendingConnects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _introductionAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _introductionsServed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _registrationLock = new();

    private LocalPeerIdentity? _identity;
    private Func<PeerInfo?> _selfRecordProvider = () => null;
    private Func<ManifestRequest, string, CancellationToken, Task<(ManifestResponse Response, ReadOnlyMemory<byte> Content)>>? _dataHandler;
    private bool _isIntroducer;
    private CancellationTokenSource _cts = new();

    public PeerSessionManager(Logger? logger = null)
    {
        _logger = logger ?? LogManager.GetCurrentClassLogger();
        _udp = new UdpSessionHost(_logger);
        _udp.TransportConnected += OnUdpTransportConnected;
    }

    /// <summary>Raised when a session's remote proved its identity. The session is registered under its UserId.</summary>
    public event Action<PeerSession>? SessionAuthenticated;

    /// <summary>Raised when a registered session closes.</summary>
    public event Action<PeerSession>? SessionClosed;

    /// <summary>Raised on incoming traffic on an authenticated session (at most every 10 seconds per session).</summary>
    public event Action<PeerSession>? SessionActivity;

    public int UdpPort => _udp.Port;

    /// <summary>Maximum number of concurrent sessions. A standalone bootstrap raises this to hold one per registered peer.</summary>
    public int MaxSessions { get; set; } = SecurityLimits.MaxSessions;

    public IReadOnlyCollection<PeerSession> Sessions => _sessions.Values.Where(s => !s.IsClosed).ToList();

    /// <summary>
    /// Configures identity and request handling.
    /// </summary>
    /// <param name="identity">This node's identity, or null for an anonymous node (standalone bootstrap).</param>
    /// <param name="selfRecordProvider">Returns this node's signed peer record for hellos.</param>
    /// <param name="dataHandler">Answers data requests (manifests, PEX, content, announce) arriving over sessions.</param>
    /// <param name="isIntroducer">Whether this node introduces its session peers to each other for hole punching.</param>
    public void Configure(
        LocalPeerIdentity? identity,
        Func<PeerInfo?> selfRecordProvider,
        Func<ManifestRequest, string, CancellationToken, Task<(ManifestResponse Response, ReadOnlyMemory<byte> Content)>> dataHandler,
        bool isIntroducer)
    {
        _identity = identity;
        _selfRecordProvider = selfRecordProvider;
        _dataHandler = dataHandler;
        _isIntroducer = isIntroducer;
    }

    /// <summary>Binds the UDP socket used for hole punching (preferring <paramref name="preferredUdpPort"/>).</summary>
    public void Start(int preferredUdpPort)
    {
        _cts = new CancellationTokenSource();
        _udp.Start(preferredUdpPort);
    }

    public void Stop()
    {
        _cts.Cancel();
        _udp.Stop();
        foreach (var session in _sessions.Values) session.Close();
        _sessions.Clear();
    }

    // ── Lookup ────────────────────────────────────────────────────────────

    /// <summary>Returns the preferred live, authenticated session with <paramref name="userId"/>, or null.</summary>
    public PeerSession? GetSession(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        return _sessions.Values
            .Where(s => !s.IsClosed && string.Equals(s.RemoteUserId, userId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Rank)
            .FirstOrDefault();
    }

    /// <summary>Returns a live session this node dialled to <paramref name="host"/>:<paramref name="port"/>, or null.</summary>
    public PeerSession? GetSessionByEndpoint(string host, int port)
    {
        var endpoint = $"{host}:{port}";
        return _sessions.Values.FirstOrDefault(s => !s.IsClosed && s.HandshakeCompleted && string.Equals(s.DialEndpoint, endpoint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolves a session for a peer by UserId, falling back to a session dialled to its endpoint.</summary>
    public PeerSession? Resolve(string? userId, string? address, int port)
    {
        return GetSession(userId) ?? (!string.IsNullOrWhiteSpace(address) && port > 0 ? GetSessionByEndpoint(address, port) : null);
    }

    public int OutboundSessionCount => _sessions.Values.Count(s => !s.IsClosed && s.IsInitiator && s.TransportKind == "tcp");

    // ── Opening sessions ──────────────────────────────────────────────────

    /// <summary>Takes over a TCP connection that was upgraded by the server.</summary>
    public void AcceptTcpSession(TcpClient client)
    {
        if (_sessions.Count >= MaxSessions)
        {
            _logger.Debug("Refusing inbound session: session limit reached.");
            client.Dispose();
            return;
        }

        StartSession(new PeerSession(new TcpFrameTransport(client), isInitiator: false, dialEndpoint: null, HandleSessionRequestAsync, _logger));
    }

    /// <summary>
    /// Opens a persistent TCP session to <paramref name="host"/>:<paramref name="port"/>, or returns the existing one.
    /// Returns null if the node is unreachable or does not support sessions.
    /// </summary>
    public async Task<PeerSession?> ConnectTcpAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var existing = GetSessionByEndpoint(host, port);
        if (existing != null) return existing;
        if (_sessions.Count >= MaxSessions) return null;

        // Concurrent callers share one attempt, so they do not open duplicate sessions to the same node.
        var endpoint = $"{host}:{port}";
        var attempt = _pendingConnects.GetOrAdd(endpoint, _ => ConnectTcpCoreAsync(host, port, cancellationToken));
        try
        {
            return await attempt;
        }
        finally
        {
            _pendingConnects.TryRemove(new KeyValuePair<string, Task<PeerSession?>>(endpoint, attempt));
        }
    }

    private async Task<PeerSession?> ConnectTcpCoreAsync(string host, int port, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var client = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            cts.CancelAfter(SecurityLimits.ConnectTimeoutMs);

            await client.ConnectAsync(host, port, cts.Token);
            var stream = client.GetStream();
            await ManifestExchangeServer.WriteMessageAsync(stream, new ManifestRequest { Type = ManifestRequestType.OpenSession }, cts.Token);
            var (bytes, _) = await ManifestExchangeServer.ReadMessageAsync(stream, cts.Token);
            if (!ManifestSerializer.DeserializeResponse(bytes).Acknowledged)
            {
                client.Dispose();
                return null;
            }

            var session = new PeerSession(new TcpFrameTransport(client), isInitiator: true, dialEndpoint: $"{host}:{port}", HandleSessionRequestAsync, _logger);
            await StartSessionAndHandshakeAsync(session);
            return session.HandshakeCompleted && !session.IsClosed ? session : null;
        }
        catch (Exception ex)
        {
            _logger.Debug("Could not open session to {0}:{1}: {2}", host, port, ex.Message);
            client.Dispose();
            return null;
        }
    }

    private void OnUdpTransportConnected(UdpFrameTransport transport)
    {
        StartSession(new PeerSession(transport, isInitiator: false, dialEndpoint: null, HandleSessionRequestAsync, _logger));
    }

    private void StartSession(PeerSession session)
    {
        _ = StartSessionAndHandshakeAsync(session);
    }

    private async Task StartSessionAndHandshakeAsync(PeerSession session)
    {
        _sessions[session.Id] = session;
        session.Closed += OnSessionClosed;
        session.Activity += s =>
        {
            if (s.IsAuthenticated) SessionActivity?.Invoke(s);
        };
        session.Start();

        var response = await session.RequestAsync(new ManifestRequest { Type = ManifestRequestType.Hello, Hello = BuildHello(session, answeringNonce: null) }, HandshakeRequestTimeoutMs);
        if (response?.Hello == null)
        {
            _logger.Debug("Session {0} handshake failed; closing.", session);
            session.Close();
            return;
        }

        ApplyRemoteHello(session, response.Hello, answersOurNonce: true);
    }

    private SessionHello BuildHello(PeerSession session, long? answeringNonce)
    {
        var self = _identity != null ? _selfRecordProvider() : null;
        return new SessionHello
        {
            Peer = self,
            Nonce = session.LocalNonce,
            Proof = self != null && _identity != null && answeringNonce.HasValue
                ? PeerRecords.SignSessionNonce(_identity.UserId, answeringNonce.Value, _identity.PrivateKey)
                : string.Empty,
            UdpPort = _udp.Port,
            IsIntroducer = _isIntroducer && _udp.IsRunning
        };
    }

    /// <summary>
    /// Records what the remote said about itself. Only a hello that answers our own nonce authenticates the remote.
    /// </summary>
    private void ApplyRemoteHello(PeerSession session, SessionHello hello, bool answersOurNonce)
    {
        session.RemoteNonce = hello.Nonce;
        session.RemoteIsIntroducer = hello.IsIntroducer;
        session.RemoteUdpPort = hello.UdpPort is > 0 and < 65536 ? hello.UdpPort : 0;

        if (!answersOurNonce) return;
        session.HandshakeCompleted = true;

        var remote = hello.Peer;
        if (remote == null || string.IsNullOrWhiteSpace(remote.UserId))
        {
            _logger.Debug("Session {0} established with anonymous node {1}", session, session.RemoteAddress);
            return;
        }

        if (!PeerRecords.VerifySessionNonce(remote.UserId, session.LocalNonce, hello.Proof, remote.PublicKey))
        {
            _logger.Warn("Session {0}: {1} failed to prove ownership of its key; closing.", session, remote.UserId);
            session.Close();
            return;
        }

        if (string.Equals(remote.UserId, _identity?.UserId, StringComparison.OrdinalIgnoreCase))
        {
            session.Close();
            return;
        }

        session.RemotePeer = remote;
        _logger.Info("Session {0} authenticated {1} ({2} via {3})", session.Id, remote.UserId, session.RemoteAddress, session.TransportKind);

        PruneDuplicates(remote.UserId);
        if (!session.IsClosed)
            SessionAuthenticated?.Invoke(session);
    }

    /// <summary>
    /// Keeps one session per remote user. Both sides rank duplicates the same way, so they close the same ones.
    /// </summary>
    private void PruneDuplicates(string userId)
    {
        List<PeerSession> duplicates;
        lock (_registrationLock)
        {
            var sessions = _sessions.Values
                .Where(s => !s.IsClosed && string.Equals(s.RemoteUserId, userId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Rank)
                .ToList();
            duplicates = sessions.Skip(1).ToList();
        }

        foreach (var duplicate in duplicates)
        {
            _logger.Debug("Closing duplicate session {0} with {1}", duplicate.Id, userId);
            duplicate.Close();
        }
    }

    private void OnSessionClosed(PeerSession session)
    {
        _sessions.TryRemove(session.Id, out _);
        if (session.IsAuthenticated || session.HandshakeCompleted)
            SessionClosed?.Invoke(session);
    }

    // ── Request handling ──────────────────────────────────────────────────

    private async Task<ManifestResponse> HandleSessionRequestAsync(ManifestRequest request, PeerSession session, CancellationToken ct)
    {
        switch (request.Type)
        {
            case ManifestRequestType.Hello when request.Hello != null:
                ApplyRemoteHello(session, request.Hello, answersOurNonce: false);
                return new ManifestResponse
                {
                    Acknowledged = true,
                    Hello = BuildHello(session, request.Hello.Nonce),
                    ObservedAddress = session.RemoteAddress
                };

            case ManifestRequestType.RequestIntroduction when request.Introduction != null:
                return await HandleIntroductionRequestAsync(session, request.Introduction, ct);

            case ManifestRequestType.IntroductionOffer when request.Introduction != null:
                return HandleIntroductionOffer(session, request.Introduction);

            case ManifestRequestType.OpenSession:
                return new ManifestResponse { Acknowledged = false };

            default:
                if (_dataHandler == null)
                    return new ManifestResponse { Acknowledged = false };
                var (response, _) = await _dataHandler(request, session.RemoteAddress, ct);
                return response;
        }
    }

    // ── Introductions (C2) ────────────────────────────────────────────────

    /// <summary>
    /// Asks the introducers this node has sessions with to introduce it to <paramref name="targetUserId"/>, then punches
    /// through and waits for an authenticated UDP session. Returns that session, or null if no introducer could help
    /// (e.g. both peers are behind symmetric NATs; see the review's C4).
    /// </summary>
    public async Task<PeerSession?> RequestIntroductionAsync(string targetUserId, CancellationToken cancellationToken = default)
    {
        var existing = GetSession(targetUserId);
        if (existing != null) return existing;
        if (_identity == null || !_udp.IsRunning) return null;

        var now = DateTime.UtcNow;
        if (_introductionAttempts.TryGetValue(targetUserId, out var last) && now - last < TimeSpan.FromSeconds(SecurityLimits.IntroductionRetryCooldownSeconds))
            return null;
        _introductionAttempts[targetUserId] = now;

        var introducers = _sessions.Values
            .Where(s => !s.IsClosed && s.HandshakeCompleted && s.RemoteIsIntroducer && s.RemoteUdpPort > 0)
            .Where(s => !string.Equals(s.RemoteUserId, targetUserId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var introducer in introducers)
        {
            var response = await introducer.RequestAsync(new ManifestRequest
            {
                Type = ManifestRequestType.RequestIntroduction,
                Introduction = new Introduction { RequesterUserId = _identity.UserId, TargetUserId = targetUserId }
            }, IntroductionRequestTimeoutMs, cancellationToken);

            var introduction = response?.Introduction;
            if (response?.Acknowledged != true || introduction == null || string.IsNullOrWhiteSpace(introduction.Token))
                continue;
            if (!IPAddress.TryParse(introducer.RemoteAddress, out var introducerAddress))
                continue;

            _logger.Info("Introducer {0} is introducing us to {1}; punching", introducer, targetUserId);
            _udp.ExpectIntroduction(introduction.Token, targetUserId, new IPEndPoint(introducerAddress, introduction.IntroducerUdpPort));

            var session = await WaitForSessionAsync(targetUserId, TimeSpan.FromSeconds(SecurityLimits.IntroductionTimeoutSeconds), cancellationToken);
            if (session != null) return session;
        }

        return null;
    }

    private async Task<PeerSession?> WaitForSessionAsync(string userId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var session = GetSession(userId);
            if (session != null) return session;
            try { await Task.Delay(100, cancellationToken); } catch (OperationCanceledException) { break; }
        }
        return GetSession(userId);
    }

    private async Task<ManifestResponse> HandleIntroductionRequestAsync(PeerSession requester, Introduction request, CancellationToken ct)
    {
        var refused = new ManifestResponse { Acknowledged = false };
        if (!_isIntroducer || !_udp.IsRunning || !requester.IsAuthenticated) return refused;
        if (!SecurityLimits.IsValidUserId(request.TargetUserId)) return refused;
        if (!AllowIntroduction(requester.RemoteUserId!)) return refused;

        var target = GetSession(request.TargetUserId);
        if (target == null)
        {
            _logger.Debug("Cannot introduce {0} to {1}: no session with the target.", requester.RemoteUserId, request.TargetUserId);
            return refused;
        }

        var token = _udp.CreateIntroductionToken();
        var offer = await target.RequestAsync(new ManifestRequest
        {
            Type = ManifestRequestType.IntroductionOffer,
            Introduction = new Introduction
            {
                RequesterUserId = requester.RemoteUserId!,
                TargetUserId = request.TargetUserId,
                Token = token,
                IntroducerUdpPort = _udp.Port
            }
        }, IntroductionRequestTimeoutMs, ct);

        if (offer?.Acknowledged != true) return refused;

        _logger.Info("Introducing {0} to {1}", requester.RemoteUserId, request.TargetUserId);
        return new ManifestResponse
        {
            Acknowledged = true,
            Introduction = new Introduction
            {
                RequesterUserId = requester.RemoteUserId!,
                TargetUserId = request.TargetUserId,
                Token = token,
                IntroducerUdpPort = _udp.Port
            }
        };
    }

    private ManifestResponse HandleIntroductionOffer(PeerSession introducer, Introduction offer)
    {
        if (_identity == null || !_udp.IsRunning || offer.IntroducerUdpPort is <= 0 or >= 65536)
            return new ManifestResponse { Acknowledged = false };
        if (!IPAddress.TryParse(introducer.RemoteAddress, out var introducerAddress))
            return new ManifestResponse { Acknowledged = false };
        if (GetSession(offer.RequesterUserId) != null)
            return new ManifestResponse { Acknowledged = false };

        _logger.Info("Introduction offer from {0}: {1} wants to connect; punching", introducer, offer.RequesterUserId);
        _udp.ExpectIntroduction(offer.Token, offer.RequesterUserId, new IPEndPoint(introducerAddress, offer.IntroducerUdpPort));
        return new ManifestResponse { Acknowledged = true };
    }

    private bool AllowIntroduction(string requesterUserId)
    {
        var queue = _introductionsServed.GetOrAdd(requesterUserId, _ => new Queue<DateTime>());
        lock (queue)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            if (queue.Count >= MaxIntroductionsPerMinutePerRequester) return false;
            queue.Enqueue(DateTime.UtcNow);
            return true;
        }
    }

    public void Dispose()
    {
        Stop();
        _udp.Dispose();
    }
}
