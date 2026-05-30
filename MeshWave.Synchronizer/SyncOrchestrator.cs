using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// SyncOrchestrator is the top-level P2P coordinator.
/// It uses PeerRouter (LAN + bootstrap + PEX) to find peers,
/// exchanges manifests over TCP, and merges verified operations.
/// </summary>
public class SyncOrchestrator : IDisposable
{
    private readonly PeerRouter _router;
    private ManifestExchangeServer? _server;
    private readonly ManifestExchangeClient _client;
    private readonly ManifestManager _manifestManager;
    private readonly PeerManifestStore _peerStore;
    private readonly ContentExchange _contentExchange;
    private readonly NatTraversalService _natTraversal;

    private LocalPeerIdentity? _identity;
    private Manifest? _localManifest;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<string> _bootstrapNodes = [];
    private PeerConnectionAttemptReport? _lastConnectionReport;

    // Tracks which trackIds have already had a Play op recorded in this process session.
    private readonly HashSet<string> _playedThisSession = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ManifestMergedEventArgs>? ManifestMerged;
    public event EventHandler? PeerCountChanged;

    /// <summary>Whether the orchestrator is currently running.</summary>
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    /// <summary>Number of currently visible peers in the routing table.</summary>
    public int ConnectedPeerCount => _router.GetPeers().Count;

    /// <summary>The local identity in use (set after StartAsync).</summary>
    public LocalPeerIdentity? Identity => _identity;

    /// <summary>Current local manifest containing this node's signed operations.</summary>
    public Manifest? LocalManifest => _localManifest;

    /// <summary>Read-only view of all peer manifests received and persisted so far.</summary>
    public IReadOnlyCollection<Manifest> PeerManifests => _peerStore.GetAll();

    /// <summary>Last peer connection attempt report from RequestContentAsync, if any.</summary>
    public PeerConnectionAttemptReport? LastConnectionAttemptReport => _lastConnectionReport;

    /// <summary>Returns the persisted manifest for a specific peer, or null if not yet received.</summary>
    public Manifest? GetPeerManifest(string userId) => _peerStore.Get(userId);

    public SyncOrchestrator(
        PeerRouter? router = null,
        ManifestExchangeServer? server = null,
        ManifestExchangeClient? client = null,
        ManifestManager? manifestManager = null,
        PeerManifestStore? peerManifestStore = null,
        ContentExchange? contentExchange = null,
        NatTraversalService? natTraversal = null)
    {
        _router = router ?? new PeerRouter();
        _server = server;
        _client = client ?? new ManifestExchangeClient(timeoutMs: SecurityLimits.ConnectTimeoutMs);
        _manifestManager = manifestManager ?? new ManifestManager();
        _peerStore = peerManifestStore ?? new PeerManifestStore();
        _contentExchange = contentExchange ?? new ContentExchange();
        _natTraversal = natTraversal ?? new NatTraversalService();
        _peerStore.LoadAll();
    }

    /// <summary>
    /// Starts P2P sync: LAN discovery, bootstrap node connections, PEX, and manifest exchange server.
    /// </summary>
    public async Task StartAsync(
        LocalPeerIdentity identity,
        Manifest localManifest,
        IReadOnlyList<string>? bootstrapNodes = null,
        CancellationToken cancellationToken = default)
    {
        _identity = identity;
        _localManifest = localManifest;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _router.PeerAdded += OnPeerAdded;
        _router.PeerRemoved += OnPeerRemoved;
        _bootstrapNodes = bootstrapNodes ?? [];

        _server ??= new ManifestExchangeServer(identity.ManifestPort);
        _server.ManifestReceived += OnManifestReceived;

        await _server.StartAsync(
            () => _localManifest,
            () => _router.GetPeersForExchange(),
            _cts.Token);

        await _router.StartAsync(identity, _bootstrapNodes, _cts.Token);
        await _natTraversal.StartAsync(identity.ManifestPort, _cts.Token);
    }

    /// <summary>
    /// Stops all P2P activity.
    /// </summary>
    public async Task StopAsync()
    {
        _router.PeerAdded -= OnPeerAdded;
        _router.PeerRemoved -= OnPeerRemoved;
        if (_server != null)
            _server.ManifestReceived -= OnManifestReceived;

        await _router.StopAsync();
        if (_server != null)
            await _server.StopAsync();
        await _natTraversal.StopAsync();
        _cts?.Cancel();
    }

    /// <summary>
    /// Returns currently visible peers from the routing table.
    /// </summary>
    public IEnumerable<PeerInfo> GetPeers() => _router.GetPeers();

    /// <summary>
    /// Clears all persisted peer manifests and in-memory cache.
    /// </summary>
    public void ClearPeerManifestCache()
    {
        _peerStore.ClearAll();
    }

    /// <summary>
    /// Manually triggers a manifest fetch from all known peers.
    /// </summary>
    public async Task SyncAllPeersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _router.GetPeers())
        {
            await TryFetchAndMergeAsync(peer, cancellationToken);
        }
    }

    /// <summary>
    /// Requests content bytes from a currently known peer by content hash.
    /// </summary>
    public async Task<byte[]?> RequestContentAsync(string peerUserId, string contentHash)
    {
        if (string.IsNullOrWhiteSpace(peerUserId) || string.IsNullOrWhiteSpace(contentHash))
            return null;

        var report = new PeerConnectionAttemptReport
        {
            PeerUserId = peerUserId,
            RequestedContentHash = contentHash,
            LocalManifestPort = _identity?.ManifestPort ?? 0,
            SuggestedLocalIp = GetPrimaryLocalIpv4()
        };
        _lastConnectionReport = report;

        var peer = _router.GetPeers().FirstOrDefault(p =>
            string.Equals(p.UserId, peerUserId, StringComparison.OrdinalIgnoreCase));

        if (peer == null)
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "routing-table-lookup",
                false,
                "Peer not present in routing table. Triggered bootstrap refresh before giving up."));

            await RefreshBootstrapAsync(report);

            peer = _router.GetPeers().FirstOrDefault(p =>
                string.Equals(p.UserId, peerUserId, StringComparison.OrdinalIgnoreCase));

            if (peer == null)
            {
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "routing-table-retry",
                    false,
                    "Peer still not discoverable after bootstrap refresh."));
                return null;
            }
        }

        report.TargetAddress = peer.Address;
        report.TargetPort = peer.Port;

        var directTcpReachable = await CanConnectTcpAsync(peer.Address, peer.Port, timeoutMs: 1_500);
        report.Attempts.Add(new PeerConnectionAttemptResult(
            "direct-tcp-probe",
            directTcpReachable,
            directTcpReachable
                ? "TCP reachability confirmed on peer manifest port."
                : "TCP probe timed out or was refused."));

        var punched = await _natTraversal.TryPunchAsync(peer.Address, peer.Port);
        report.Attempts.Add(new PeerConnectionAttemptResult(
            "udp-hole-punch",
            punched,
            punched
                ? "UDP punch ACK received from peer."
                : "No UDP punch ACK observed; continuing with direct TCP attempt."));

        var bytes = await _contentExchange.RequestContentAsync(peer.Address, peer.Port, contentHash);
        var succeeded = bytes != null && bytes.Length > 0;
        report.Attempts.Add(new PeerConnectionAttemptResult(
            "content-request",
            succeeded,
            succeeded
                ? $"Received {bytes!.Length} bytes from peer."
                : "Peer did not return content bytes."));

        if (!succeeded)
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "nat-guidance",
                false,
                BuildNatGuidance(peer.Address, peer.Port, _identity?.ManifestPort ?? 0, report.SuggestedLocalIp ?? "127.0.0.1")));
        }

        return bytes;
    }

    private void OnPeerAdded(object? sender, PeerInfo peer)
    {
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
        _ = Task.Run(() => TryFetchAndMergeAsync(peer, _cts?.Token ?? CancellationToken.None));
    }

    private void OnPeerRemoved(object? sender, string userId)
    {
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
    {
        // Ignore pushes from ourselves
        if (e.Manifest.UserId == _identity?.UserId) return;

        var peer = _router.GetPeers().FirstOrDefault(p => p.UserId == e.Manifest.UserId);
        if (peer == null || string.IsNullOrWhiteSpace(peer.PublicKeyPem)) return;

        TryMerge(e.Manifest, peer.PublicKeyPem);
    }

    private async Task TryFetchAndMergeAsync(PeerInfo peer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peer.PublicKeyPem)) return;
        if (peer.UserId == _identity?.UserId) return;

        try
        {
            var remoteManifest = await _client.FetchManifestAsync(peer.Address, peer.Port, ct);
            if (remoteManifest == null) return;
            TryMerge(remoteManifest, peer.PublicKeyPem);
        }
        catch { /* peer unreachable – will retry on next cycle */ }
    }

    private async Task RefreshBootstrapAsync(PeerConnectionAttemptReport report)
    {
        if (_bootstrapNodes.Count == 0)
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "bootstrap-refresh",
                false,
                "No bootstrap nodes configured."));
            return;
        }

        var refreshed = false;
        foreach (var endpoint in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
        {
            if (!TryParseEndpoint(endpoint, out var host, out var port))
            {
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "bootstrap-refresh",
                    false,
                    $"Skipped invalid bootstrap endpoint '{endpoint}'."));
                continue;
            }

            try
            {
                var peers = await _client.FetchPeersAsync(host, port);
                _router.LearnPeers(peers);
                refreshed = true;
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "bootstrap-refresh",
                    true,
                    $"Fetched {peers.Count} peers from bootstrap {host}:{port}."));
            }
            catch (Exception ex)
            {
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "bootstrap-refresh",
                    false,
                    $"Bootstrap {host}:{port} failed: {ex.Message}"));
            }
        }

        if (!refreshed)
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "bootstrap-refresh",
                false,
                "Bootstrap refresh completed without usable peer data."));
        }
    }

    private static bool TryParseEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon <= 0)
            return false;

        host = endpoint[..lastColon];
        return int.TryParse(endpoint[(lastColon + 1)..], out port) && port > 0 && port < 65536;
    }

    private static async Task<bool> CanConnectTcpAsync(string address, int port, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(address) || port <= 0)
            return false;

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            await client.ConnectAsync(address, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildNatGuidance(string peerAddress, int peerPort, int localPort, string localIp)
    {
        var local = localPort > 0 ? localPort : ManifestExchangeServer.DefaultPort;
        return $"Could not establish a direct peer content connection after all automatic attempts. Suggested router/NAT mapping: forward TCP+UDP {local} to {localIp}:{local}. Ask remote peer owner to forward TCP+UDP {peerPort} to {peerAddress}:{peerPort}. If both peers are behind symmetric NAT, run one peer with a public IP or use a relay-capable bootstrap in future.";
    }

    private static string? GetPrimaryLocalIpv4()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.Speed);

            foreach (var nic in interfaces)
            {
                var ip = nic.GetIPProperties().UnicastAddresses
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

                if (ip != null)
                    return ip.ToString();
            }
        }
        catch
        {
            // best-effort diagnostics only
        }

        return null;
    }

    /// <summary>
    /// Records a signed Play operation for the given track in the local manifest.
    /// Rate-capped to one call per track per process session so that repeated pauses
    /// and resumes do not inflate the count.
    /// Does nothing when P2P is not started or the track has already been counted this session.
    /// </summary>
    /// <param name="trackId">Stable identifier for the track (e.g. filename without extension).</param>
    /// <param name="title">Track title stored as metadata.</param>
    /// <param name="artist">Artist name stored as metadata.</param>
    /// <returns><c>true</c> if a new Play operation was appended; <c>false</c> if rate-capped or not ready.</returns>
    public bool RecordPlay(string trackId, string title, string artist)
    {
        if (_localManifest == null || _identity == null) return false;
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        if (!_playedThisSession.Add(trackId)) return false;   // already counted this session

        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Play,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            new Dictionary<string, string>
            {
                ["title"]  = SecurityLimits.Truncate(title,  SecurityLimits.MaxTrackTitleLength),
                ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength)
            },
            _identity.PrivateKeyPem);

        return true;
    }

    /// <summary>
    /// Announces a track release to the network by appending a signed Create operation to the local manifest.
    /// Automatically stamps <c>releasedAt</c> (ISO-8601 UTC) into the metadata dictionary if not already set.
    /// Call this when the user marks a track as released and wants peers to discover it.
    /// </summary>
    public void AnnounceTrack(string trackId, string contentHash, Dictionary<string, string>? metadata = null)
    {
        if (_localManifest == null || _identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta.TryAdd("releasedAt", DateTime.UtcNow.ToString("O"));
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Create,
            trackId,
            "Track",
            contentHash,
            meta,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Announces an album release to the network.
    /// Automatically stamps <c>releasedAt</c> (ISO-8601 UTC) into the metadata dictionary if not already set.
    /// </summary>
    public void AnnounceAlbum(string albumId, string? contentHash, Dictionary<string, string>? metadata = null)
    {
        if (_localManifest == null || _identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta.TryAdd("releasedAt", DateTime.UtcNow.ToString("O"));
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Create,
            albumId,
            "Album",
            contentHash,
            meta,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed Follow op for <paramref name="targetUserId"/> to the local manifest.
    /// Safe to call multiple times — duplicate ops are ignored during merge.
    /// </summary>
    public void RecordFollow(string targetUserId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Follow,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed FriendAdd op for <paramref name="targetUserId"/>.
    /// </summary>
    public void RecordFriendAdd(string targetUserId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.FriendAdd,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed FriendRemove op for <paramref name="targetUserId"/>.
    /// </summary>
    public void RecordFriendRemove(string targetUserId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.FriendRemove,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed GroupJoin op for <paramref name="groupId"/>.
    /// </summary>
    public void RecordGroupJoin(string groupId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.GroupJoin,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed GroupLeave op for <paramref name="groupId"/>.
    /// </summary>
    public void RecordGroupLeave(string groupId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.GroupLeave,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed Unfollow op for <paramref name="targetUserId"/> to the local manifest.
    /// </summary>
    public void RecordUnfollow(string targetUserId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;
        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Unfollow,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed Comment op for a track to the local manifest.
    /// </summary>
    public string? RecordComment(string trackId, string commentText, string? replyToId = null, Dictionary<string, string>? metadata = null)
    {
        if (_localManifest == null || _identity == null) return null;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(commentText)) return null;

        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta["text"] = SecurityLimits.Truncate(commentText, SecurityLimits.MaxCommentTextLength);
        if (!string.IsNullOrWhiteSpace(replyToId))
            meta["replyToId"] = SecurityLimits.Truncate(replyToId, SecurityLimits.MaxOperationIdLength);

        var op = _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Comment,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: meta,
            _identity.PrivateKeyPem);

        return op.OperationId;
    }

    /// <summary>
    /// Appends a signed CommentDelete op for a previously authored comment operation.
    /// </summary>
    public void RecordCommentDelete(string trackId, string commentOperationId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(commentOperationId)) return;

        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.CommentDelete,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: new Dictionary<string, string>
            {
                ["commentOperationId"] = SecurityLimits.Truncate(commentOperationId, SecurityLimits.MaxOperationIdLength)
            },
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed Like op for a track.
    /// </summary>
    public void RecordLike(string trackId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId)) return;

        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Like,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Appends a signed Unlike op for a track.
    /// </summary>
    public void RecordUnlike(string trackId)
    {
        if (_localManifest == null || _identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId)) return;

        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Unlike,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: null,
            _identity.PrivateKeyPem);
    }

    /// <summary>
    /// Broadcasts the user's current profile as a signed Profile op.
    /// Peers receiving this op can update their local view of the user's identity.
    /// </summary>
    public void BroadcastProfile(string displayName, bool isArtist, string bio, string website, string? bannerImageHash)
    {
        if (_localManifest == null || _identity == null) return;
        var meta = new Dictionary<string, string>
        {
            ["displayName"] = SecurityLimits.Truncate(displayName, SecurityLimits.MaxArtistNameLength),
            ["isArtist"]    = isArtist.ToString(),
            ["bio"]         = SecurityLimits.Truncate(bio, 1000),
            ["website"]     = SecurityLimits.Truncate(website, 256),
        };
        if (!string.IsNullOrWhiteSpace(bannerImageHash))
            meta["bannerImageHash"] = bannerImageHash;

        _manifestManager.AppendSignedOperation(
            _localManifest,
            ManifestOperationType.Profile,
            _identity.UserId,
            "User",
            contentHash: bannerImageHash,
            meta,
            _identity.PrivateKeyPem);
    }

    private void TryMerge(Manifest remote, string publicKeyPem)
    {
        if (remote.UserId == _identity?.UserId) return;

        var added = _peerStore.MergeAndSave(remote, publicKeyPem, _manifestManager);
        if (added > 0)
            ManifestMerged?.Invoke(this, new ManifestMergedEventArgs(remote.UserId, added));
    }

    public void Dispose()
    {
        _router.Dispose();
        _server?.Dispose();
        _natTraversal.Dispose();
        _cts?.Dispose();
    }
}

public class ManifestMergedEventArgs(string userId, int operationsAdded) : EventArgs
{
    public string UserId { get; } = userId;
    public int OperationsAdded { get; } = operationsAdded;
}

public sealed class PeerConnectionAttemptReport
{
    public required string PeerUserId { get; init; }
    public required string RequestedContentHash { get; init; }
    public string? TargetAddress { get; set; }
    public int TargetPort { get; set; }
    public int LocalManifestPort { get; init; }
    public string? SuggestedLocalIp { get; init; }
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
    public List<PeerConnectionAttemptResult> Attempts { get; } = [];

    public string BuildUserFacingSummary()
    {
        var attemptSummary = string.Join(" | ", Attempts.Select(a => $"{a.Method}: {(a.Success ? "ok" : "fail")}"));
        var finalGuidance = Attempts.LastOrDefault(a => string.Equals(a.Method, "nat-guidance", StringComparison.OrdinalIgnoreCase))?.Details;
        return string.IsNullOrWhiteSpace(finalGuidance)
            ? attemptSummary
            : $"{attemptSummary}{Environment.NewLine}{finalGuidance}";
    }
}

public sealed record PeerConnectionAttemptResult(string Method, bool Success, string Details);
