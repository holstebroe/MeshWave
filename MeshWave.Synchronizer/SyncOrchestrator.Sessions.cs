using System.Net;
using MeshWave.Common.Core;
using MeshWave.Common.Core.P2P;

namespace MeshWave.Synchronizer;

public partial class SyncOrchestrator
{
    private const int SessionMaintenanceIntervalSeconds = 20;
    private const int MaxIntroductionsPerMaintenanceCycle = 3;

    private readonly Lock _selfRecordLock = new();
    private PeerInfo? _selfRecord;
    private string? _observedAddress;
    private bool? _reachable;

    /// <summary>
    /// This peer's own signed record as shared with other peers: the port is 0 (outbound-only) when we have no
    /// listener, or when a bootstrap's dial-back found our port unreachable.
    /// </summary>
    private PeerInfo? GetSelfRecord()
    {
        var candidatePort = CandidatePort();
        return GetSignedSelfRecord(_reachable == false ? 0 : candidatePort);
    }

    /// <summary>
    /// The record announced to bootstrap nodes. It always carries the port we listen on (or the router mapped),
    /// so the bootstrap re-checks reachability on every heartbeat and we recover once the port opens.
    /// </summary>
    private PeerInfo? BuildAnnouncement()
    {
        return GetSignedSelfRecord(CandidatePort());
    }

    private int CandidatePort()
    {
        if (!_actAsListener || Identity == null) return 0;
        return _natTraversal.ExternalPort ?? Identity.ManifestPort;
    }

    private PeerInfo? GetSignedSelfRecord(int port)
    {
        var identity = Identity;
        if (identity == null) return null;

        // A router-reported external address only applies when the router actually forwards our port.
        var address = (_natTraversal.ExternalPort != null ? ExternalIPAddress : null) ?? _observedAddress ?? string.Empty;
        var displayName = SecurityLimits.Truncate(identity.DisplayName ?? identity.UserId, SecurityLimits.MaxDisplayNameLength);

        lock (_selfRecordLock)
        {
            var cached = _selfRecord;
            if (cached != null && cached.Address == address && cached.Port == port && cached.DisplayName == displayName
                && cached.SignedAtUtc > DateTime.UtcNow.AddMinutes(-SecurityLimits.PeerRecordResignMinutes))
                return PeerRecords.Clone(cached);

            var record = new PeerInfo
            {
                UserId = identity.UserId,
                DisplayName = displayName,
                Address = address,
                Port = port,
                PublicKey = identity.PublicKey,
                EncryptionPublicKey = identity.EncryptionPublicKey
            };
            _selfRecord = PeerRecords.Sign(record, identity.PrivateKey, DateTime.UtcNow);
            return PeerRecords.Clone(_selfRecord);
        }
    }

    private void OnSelfObserved(object? sender, AnnounceResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ObservedAddress) && IPAddress.TryParse(result.ObservedAddress, out _))
            _observedAddress = result.ObservedAddress;

        if (result.DialBackSucceeded is { } reachable && reachable != _reachable)
        {
            _reachable = reachable;
            if (reachable)
                _logger.Info("Bootstrap dial-back succeeded: port {0} is reachable from outside.", CandidatePort());
            else
                _logger.Warn("Bootstrap dial-back to port {0} failed: announcing as outbound-only (port 0). Peers reach us over persistent sessions.", CandidatePort());
        }
    }

    private void OnSessionAuthenticated(PeerSession session)
    {
        var remote = session.RemotePeer;
        if (remote == null) return;

        // Trust the address we observe on the session over the one in the record.
        var observed = PeerRecords.Clone(remote);
        if (session.TransportKind == "tcp" && !string.IsNullOrWhiteSpace(session.RemoteAddress) && observed.Address != session.RemoteAddress)
        {
            observed = PeerRecords.Unsigned(observed);
            observed.Address = session.RemoteAddress;
        }
        else if (string.IsNullOrWhiteSpace(observed.Address))
        {
            observed = PeerRecords.Unsigned(observed);
            observed.Address = session.RemoteAddress;
        }

        _router.LearnPeerDirect(observed);
        RecordPeerMessage(remote.UserId, "Session", success: true,
            $"Persistent {session.TransportKind} session established with {session.RemoteAddress}.");

        // Catch up over the new session; for a newly punched peer this is the first exchange ever.
        var routed = _router.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, remote.UserId, StringComparison.OrdinalIgnoreCase));
        if (routed != null)
            _ = Task.Run(() => SyncWithPeerAsync(routed, _cts?.Token ?? CancellationToken.None));
    }

    private void OnSessionActivity(PeerSession session)
    {
        if (session.RemoteUserId != null)
            _router.MarkContacted(session.RemoteUserId);
    }

    /// <summary>Whether we can send requests to this peer: over a session, or by dialling its open port.</summary>
    private bool HasRoute(PeerInfo peer)
    {
        return _sessions.GetSession(peer.UserId) != null || PeerRouter.IsDialable(peer);
    }

    private async Task SessionMaintenanceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(SessionMaintenanceIntervalSeconds), ct);
                await EnsureSessionsAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.Debug("Session maintenance failed: {0}", ex.Message);
            }
    }

    /// <summary>
    /// Keeps a control session to every bootstrap node, sessions to a few dialable neighbours, and tries to get
    /// hole-punched sessions to peers that have no open port.
    /// </summary>
    private async Task EnsureSessionsAsync(CancellationToken ct)
    {
        var bootstrapConnects = new List<Task>();
        foreach (var bootstrap in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
            if (PeerRouter.TryParseEndpoint(bootstrap, out var host, out var port) && _sessions.GetSessionByEndpoint(host, port) == null)
                bootstrapConnects.Add(_sessions.ConnectTcpAsync(host, port, ct));
        await Task.WhenAll(bootstrapConnects);

        foreach (var peer in _router.GetPeers().Where(p => !IsBootstrapEntry(p)).OrderBy(_ => Guid.NewGuid()).ToList())
        {
            if (_sessions.GetSession(peer.UserId) != null) continue;
            if (PeerRouter.IsDialable(peer))
            {
                if (_sessions.OutboundSessionCount < SecurityLimits.MaxOutboundNeighbourSessions + _bootstrapNodes.Count)
                    await _sessions.ConnectTcpAsync(peer.Address, peer.Port, ct);
            }
        }

        var unreachable = _router.GetPeers()
            .Where(p => !IsBootstrapEntry(p) && !PeerRouter.IsDialable(p) && _sessions.GetSession(p.UserId) == null)
            .Take(MaxIntroductionsPerMaintenanceCycle)
            .ToList();
        await Task.WhenAll(unreachable.Select(p => _sessions.RequestIntroductionAsync(p.UserId, ct)));
    }

    /// <summary>Opens a session to a newly discovered peer: TCP if it has an open port, otherwise via an introducer.</summary>
    private async Task TryOpenSessionAsync(PeerInfo peer, CancellationToken ct)
    {
        if (IsBootstrapEntry(peer) || _sessions.GetSession(peer.UserId) != null) return;

        if (PeerRouter.IsDialable(peer))
        {
            if (_sessions.OutboundSessionCount < SecurityLimits.MaxOutboundNeighbourSessions + _bootstrapNodes.Count)
                await _sessions.ConnectTcpAsync(peer.Address, peer.Port, ct);
        }
        else
        {
            await _sessions.RequestIntroductionAsync(peer.UserId, ct);
        }
    }

    private static bool IsBootstrapEntry(PeerInfo peer)
    {
        return peer.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase);
    }
}
