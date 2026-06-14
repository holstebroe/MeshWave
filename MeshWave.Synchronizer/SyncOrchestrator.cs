using MeshWave.Common.Core;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Storage;
using NLog;
using Logger = NLog.Logger;

namespace MeshWave.Synchronizer;

/// <summary>
/// SyncOrchestrator is the top-level P2P coordinator.
/// It uses PeerRouter (LAN + bootstrap + PEX) to find peers,
/// exchanges manifests over TCP, and merges verified operations.
/// </summary>
public class SyncOrchestrator : ISyncBrowseClient, IDisposable
{
    private readonly Logger _logger;
    private readonly PeerRouter _router;
    private ManifestExchangeServer? _server;
    private readonly ManifestExchangeClient _client;
    private readonly ManifestManager _manifestManager;
    private readonly IManifestStore _peerStore;
    private readonly ContentExchange _contentExchange;
    private readonly NatTraversalService _natTraversal;
    private readonly IMeshWaveEnvironment _environment;

    private readonly Dictionary<ManifestStreamType, Manifest> _localManifests = [];
    private bool _actAsListener;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<string> _bootstrapNodes = [];
    private int _inboundManifestPushCount;
    private int _outboundManifestFetchCount;
    private Func<string, byte[]?>? _contentProvider;

    private readonly Lock _diagnosticsLock = new();
    private readonly Dictionary<string, Queue<PeerMessageLogEntry>> _peerMessageLogs = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxMessageLogEntriesPerPeer = 100;

    // Tracks which trackIds have already had a Play op recorded in this process session.
    private readonly HashSet<string> _playedThisSession = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ManifestMergedEventArgs>? ManifestMerged;
    public event EventHandler? PeerCountChanged;

    /// <summary>Whether the orchestrator is currently running.</summary>
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    /// <summary>Number of currently visible peers in the routing table.</summary>
    public int ConnectedPeerCount => _router.GetPeers().Count;

    /// <summary>The local identity in use (set after StartAsync).</summary>
    public LocalPeerIdentity? Identity { get; private set; }

    /// <summary>Returns the primary content manifest for compatibility.</summary>
    public Manifest? LocalManifest => GetLocalManifest(ManifestStreamType.Content);

    /// <summary>Returns the current local manifest for the given stream type.</summary>
    public Manifest? GetLocalManifest(ManifestStreamType streamType)
    {
        return _localManifests.GetValueOrDefault(streamType);
    }

    /// <summary>Read-only view of all peer manifests received and persisted so far.</summary>
    public IReadOnlyCollection<Manifest> PeerManifests => _peerStore.GetAll();

    /// <summary>The community user repository for profile lookup.</summary>
    public UserRepository? UserRepository { get; private set; }

    /// <summary>Last peer connection attempt report from RequestContentAsync, if any.</summary>
    public PeerConnectionAttemptReport? LastConnectionAttemptReport { get; private set; }

    /// <summary>The shared catalogue service for global metadata lookup.</summary>
    public ICatalogueService CatalogueService { get; }

    public int LocalPublishedTrackCount => CountPublishedItems(GetLocalManifest(ManifestStreamType.Content), "Track");
    public int LocalPublishedAlbumCount => CountPublishedItems(GetLocalManifest(ManifestStreamType.Content), "Album");

    public int InboundManifestPushCount => _inboundManifestPushCount;
    public int OutboundManifestFetchCount => _outboundManifestFetchCount;
    public int BootstrapPeerCount => _router.GetPeers().Count(p => p.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase));
    public int MeshPeerCount => Math.Max(0, ConnectedPeerCount - BootstrapPeerCount);
    public string NatStatus => _natTraversal.NatStatus;
    public string? ExternalIPAddress => _natTraversal.ExternalIPAddress;
    public string? MappingProtocol => _natTraversal.MappingProtocol;

    /// <summary>Returns the persisted manifest for a specific peer and stream, or null if not yet received.</summary>
    public Manifest? GetPeerManifest(string userId, ManifestStreamType streamType = ManifestStreamType.Content)
    {
        return _peerStore.Get(userId, streamType);
    }

    public IReadOnlyCollection<PeerDiagnosticsSnapshot> GetPeerDiagnosticsSnapshots()
    {
        lock (_diagnosticsLock)
        {
            var routedPeers = _router.GetPeers().ToDictionary(p => p.UserId, StringComparer.OrdinalIgnoreCase);
            var manifests = _peerStore.GetAll().ToDictionary(m => m.UserId, StringComparer.OrdinalIgnoreCase);

            var allUserIds = routedPeers.Keys
                .Concat(manifests.Keys)
                .Concat(_peerMessageLogs.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => !string.Equals(id, Identity?.UserId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return allUserIds
                .Select(userId =>
                {
                    routedPeers.TryGetValue(userId, out var peer);
                    manifests.TryGetValue(userId, out var manifest);
                    _peerMessageLogs.TryGetValue(userId, out var queue);

                    var logs = queue?.ToList() ?? [];
                    var isBootstrap = userId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase);

                    return new PeerDiagnosticsSnapshot
                    {
                        UserId = userId,
                        DisplayName = ResolveDisplayName(manifest, peer),
                        Address = peer?.Address ?? string.Empty,
                        Port = peer?.Port ?? 0,
                        IsOnline = peer != null,
                        IsBootstrap = isBootstrap,
                        HasManifest = manifest != null,
                        PublishedTrackCount = CountPublishedItems(manifest, "Track"),
                        PublishedAlbumCount = CountPublishedItems(manifest, "Album"),
                        OperationCount = manifest?.Operations.Count ?? 0,
                        RecentMessages = logs
                    };
                })
                .OrderByDescending(p => p.IsOnline)
                .ThenBy(p => p.IsBootstrap)
                .ThenByDescending(p => p.PublishedTrackCount)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public SyncOrchestrator(
        PeerRouter router,
        ManifestExchangeClient client,
        ManifestManager manifestManager,
        IManifestStore peerManifestStore,
        ContentExchange contentExchange,
        NatTraversalService natTraversal,
        ICatalogueService catalogueService,
        IMeshWaveEnvironment environment,
        ManifestExchangeServer? server = null,
        UserRepository? userRepository = null,
        Logger? logger = null)
    {
        _logger = logger ?? LogManager.GetCurrentClassLogger();
        _environment = environment;

        _router = router;
        _server = server;
        _client = client;
        _manifestManager = manifestManager;
        UserRepository = userRepository;
        CatalogueService = catalogueService;

        _peerStore = peerManifestStore;

        _contentExchange = contentExchange;
        _natTraversal = natTraversal;
        _peerStore.LoadAll();
    }

    public void SetUserRepository(UserRepository repo)
    {
        UserRepository = repo;
    }

    /// <summary>
    /// Starts P2P sync: LAN discovery, bootstrap node connections, PEX, and manifest exchange server.
    /// </summary>
    /// <param name="contentProvider">
    /// Optional callback that returns raw file bytes for a given content hash.
    /// When provided, this node will serve file download requests from peers.
    /// </param>
    public async Task StartAsync(
        LocalPeerIdentity identity,
        IEnumerable<Manifest> localManifests,
        IReadOnlyList<string>? bootstrapNodes = null,
        bool actAsListener = true,
        Func<string, byte[]?>? contentProvider = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("Starting SyncOrchestrator for user {0} (listener={1})", identity.UserId, actAsListener);
        Identity = identity;

        _localManifests.Clear();
        foreach (var m in localManifests) _localManifests[m.StreamType] = m;

        // Ensure all streams are present
        foreach (ManifestStreamType streamType in Enum.GetValues(typeof(ManifestStreamType)))
            if (!_localManifests.ContainsKey(streamType))
            {
                var m = LoadLocalManifest(identity.UserId, streamType) ?? _manifestManager.CreateManifest(identity.UserId);
                m.StreamType = streamType;
                _localManifests[streamType] = m;
            }

        _actAsListener = actAsListener;
        _contentProvider = contentProvider;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _router.PeerAdded += OnPeerAdded;
        _router.PeerRemoved += OnPeerRemoved;
        _bootstrapNodes = bootstrapNodes ?? [];

        if (_actAsListener)
        {
            _server ??= new ManifestExchangeServer(identity.ManifestPort, logger: _logger);
            _server.ManifestReceived += OnManifestReceived;

            await _natTraversal.StartAsync(identity.ManifestPort, _cts.Token);
            await _natTraversal.SetupPortMappingAsync(identity.ManifestPort, _cts.Token);

            await _server.StartAsync(
                streamType => _localManifests.GetValueOrDefault(streamType),
                () => _router.GetPeersForExchange(),
                rendezvousProvider: null,
                contentProvider: _contentProvider,
                relayedManifestProvider: (targetUserId, streamType) => null,
                cancellationToken: _cts.Token);
        }

        await _router.StartAsync(identity, _bootstrapNodes, _cts.Token);

        foreach (var manifest in _localManifests.Values) await CatalogueService.IngestAsync(manifest);
    }

    /// <summary>
    /// Stops all P2P activity.
    /// </summary>
    public async Task StopAsync()
    {
        _logger.Info("Stopping SyncOrchestrator");
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
    public IEnumerable<PeerInfo> GetPeers()
    {
        return _router.GetPeers();
    }

    /// <summary>
    /// Clears all persisted peer manifests and in-memory cache.
    /// </summary>
    public void ClearPeerManifestCache()
    {
        _peerStore.ClearAll();
    }

    /// <summary>
    /// Saves all local manifests to disk.
    /// </summary>
    public void SaveLocalManifests()
    {
        if (Identity == null) return;
        foreach (var kvp in _localManifests) SaveLocalManifest(kvp.Value);
    }

    private void SaveLocalManifest(Manifest manifest)
    {
        var path = BuildLocalManifestPath(manifest.UserId, manifest.StreamType);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            lock (manifest)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(manifest));
            }
        }
        catch { /* best-effort disk write */ }
    }

    /// <summary>
    /// Compatibility method for saving local manifest.
    /// </summary>
    public void SaveLocalManifest()
    {
        SaveLocalManifests();
    }

    /// <summary>
    /// Loads a previously persisted local manifest for the given userId and stream.
    /// Returns null if no persisted manifest exists.
    /// </summary>
    public Manifest? LoadLocalManifest(string userId, ManifestStreamType streamType)
    {
        var path = BuildLocalManifestPath(userId, streamType);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Manifest>(json);
        }
        catch { return null; }
    }

    private string BuildLocalManifestPath(string userId, ManifestStreamType streamType)
    {
        var safeName = string.Concat(userId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var suffix = streamType.ToString().ToLowerInvariant();
        var baseFolder = UserRepository?.BaseDataFolder ?? _environment.GetAppDataRoot();
        var dir = Path.Combine(baseFolder, "LocalManifests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{safeName}.{suffix}.json");
    }

    /// <summary>
    /// Manually triggers a manifest fetch from all known peers.
    /// </summary>
    public async Task SyncAllPeersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _router.GetPeers()) await TryFetchAndMergeAsync(peer, cancellationToken);
    }

    /// <summary>
    /// Requests content bytes from a currently known peer by content hash.
    /// </summary>
    public async Task<bool> IsContentAvailableLocallyAsync(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash)) return false;
        var peers = await CatalogueService.GetPeersForContentAsync(contentHash);
        return peers.Any(uid => string.Equals(uid, Identity?.UserId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(Stream? Stream, long ContentLength)> RequestContentStreamAsync(string peerUserId, string contentHash)
    {
        _logger.Debug("RequestContentStreamAsync: peer={0}, hash={1}", peerUserId, contentHash);
        if (string.IsNullOrWhiteSpace(peerUserId) || string.IsNullOrWhiteSpace(contentHash))
            return (null, 0);

        var (peer, report) = await PrepareConnectionAsync(peerUserId, contentHash);
        if (peer == null) return (null, 0);

        var (stream, length, failureReason) = await _client.RequestContentStreamAsync(peer.Address, peer.Port, contentHash);
        var succeeded = stream != null;

        if (!succeeded)
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "content-stream-request-initial",
                false,
                $"Initial stream request failed: {failureReason}"));

            await RefreshBootstrapAsync(report);
            var refreshedPeer = _router.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, peerUserId, StringComparison.OrdinalIgnoreCase));
            if (refreshedPeer != null && (!string.Equals(refreshedPeer.Address, peer.Address, StringComparison.OrdinalIgnoreCase) || refreshedPeer.Port != peer.Port))
            {
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "content-stream-request-endpoint-refresh",
                    true,
                    "Routing endpoint changed; retrying stream request."));

                peer = refreshedPeer;
                report.TargetAddress = peer.Address;
                report.TargetPort = peer.Port;

                (stream, length, failureReason) = await _client.RequestContentStreamAsync(peer.Address, peer.Port, contentHash);
                succeeded = stream != null;
            }
        }

        if (!succeeded) _logger.Warn("Content stream request failed for peer {0}: {1}", peerUserId, failureReason);

        report.Attempts.Add(new PeerConnectionAttemptResult(
            "content-stream-request",
            succeeded,
            succeeded
                ? $"Stream opened successfully ({length} bytes expected)."
                : $"Peer did not open content stream: {failureReason}"));

        RecordPeerMessage(peer.UserId, "RequestContentStream", succeeded,
            succeeded
                ? $"Content stream started from {peer.Address}:{peer.Port}."
                : $"Content stream failed from {peer.Address}:{peer.Port}. Reason: {failureReason}");

        if (!succeeded)
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "nat-guidance",
                false,
                BuildNatGuidance(peer.Address, peer.Port, Identity?.ManifestPort ?? 0, report.SuggestedLocalIp ?? "127.0.0.1")));

        return (stream, length);
    }

    private async Task<(PeerInfo? Peer, PeerConnectionAttemptReport Report)> PrepareConnectionAsync(string peerUserId, string contentHash)
    {
        _logger.Info("Preparing connection to peer {0} for content {1}", peerUserId, contentHash);
        var report = new PeerConnectionAttemptReport
        {
            PeerUserId = peerUserId,
            RequestedContentHash = contentHash,
            LocalManifestPort = Identity?.ManifestPort ?? 0,
            SuggestedLocalIp = GetPrimaryLocalIpv4()
        };
        LastConnectionAttemptReport = report;

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
                return (null, report);
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

        if (directTcpReachable) _logger.Info("Established direct TCP connection to {0}:{1}", peer.Address, peer.Port);

        var punched = await _natTraversal.TryPunchAsync(peer.Address, peer.Port);
        report.Attempts.Add(new PeerConnectionAttemptResult(
            "udp-hole-punch",
            punched,
            punched
                ? "UDP punch ACK received from peer."
                : "No UDP punch ACK observed; continuing with direct TCP attempt."));

        if (punched) _logger.Info("Established UDP hole-punched connection to {0}:{1}", peer.Address, peer.Port);

        if (!punched && !directTcpReachable)
        {
            var rendezvous = await RequestBootstrapRendezvousAsync(peerUserId, report);
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "bootstrap-rendezvous",
                rendezvous?.Success == true,
                rendezvous?.Success == true
                    ? $"Session {rendezvous.SessionId} issued (probe-start={rendezvous.ProbeStartUtc:O}, window={rendezvous.ProbeWindowMs}ms, expires={rendezvous.ExpiresAtUtc:O}). {rendezvous.Message}"
                    : "Bootstrap rendezvous unavailable or failed."));

            if (rendezvous?.Success == true)
            {
                await WaitForProbeWindowAsync(rendezvous, report);
                var synchronizedPunch = await _natTraversal.TryPunchAsync(peer.Address, peer.Port);
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "udp-hole-punch-rendezvous-window",
                    synchronizedPunch,
                    synchronizedPunch
                        ? "UDP punch ACK received during coordinated rendezvous window."
                        : "No ACK during coordinated rendezvous window."));

                if (synchronizedPunch) _logger.Info("Established synchronized UDP hole-punched connection to {0}:{1} via rendezvous", peer.Address, peer.Port);
            }
        }

        return (peer, report);
    }

    public async Task<byte[]?> RequestContentAsync(string peerUserId, string contentHash)
    {
        if (string.IsNullOrWhiteSpace(peerUserId) || string.IsNullOrWhiteSpace(contentHash))
            return null;

        var (peer, report) = await PrepareConnectionAsync(peerUserId, contentHash);
        if (peer == null) return null;

        var (bytes, failureReason) = await _client.RequestContentAsync(peer.Address, peer.Port, contentHash);
        var succeeded = bytes != null && bytes.Length > 0;

        if (!succeeded)
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "content-request-initial",
                false,
                $"Initial content request failed: {failureReason}"));

            await RefreshBootstrapAsync(report);
            var refreshedPeer = _router.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, peerUserId, StringComparison.OrdinalIgnoreCase));
            if (refreshedPeer != null && (!string.Equals(refreshedPeer.Address, peer.Address, StringComparison.OrdinalIgnoreCase) || refreshedPeer.Port != peer.Port))
            {
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "content-request-endpoint-refresh",
                    true,
                    $"Routing endpoint changed from {peer.Address}:{peer.Port} to {refreshedPeer.Address}:{refreshedPeer.Port}; retrying content request."));

                peer = refreshedPeer;
                report.TargetAddress = peer.Address;
                report.TargetPort = peer.Port;

                (bytes, failureReason) = await _client.RequestContentAsync(peer.Address, peer.Port, contentHash);
                succeeded = bytes != null && bytes.Length > 0;
            }
        }

        if (!succeeded) _logger.Warn("Content request failed for peer {0}: {1}", peerUserId, failureReason);

        report.Attempts.Add(new PeerConnectionAttemptResult(
            "content-request",
            succeeded,
            succeeded
                ? $"Received {bytes!.Length} bytes from peer."
                : $"Peer did not return content bytes: {failureReason}"));
        RecordPeerMessage(peer.UserId, "RequestContent", succeeded,
            succeeded
                ? $"Content request succeeded ({bytes!.Length} bytes) from {peer.Address}:{peer.Port}."
                : $"Content request failed from {peer.Address}:{peer.Port} for hash {contentHash}. Reason: {failureReason}");

        if (!succeeded)
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "nat-guidance",
                false,
                BuildNatGuidance(peer.Address, peer.Port, Identity?.ManifestPort ?? 0, report.SuggestedLocalIp ?? "127.0.0.1")));

        return bytes;
    }

    private void OnPeerAdded(object? sender, PeerInfo peer)
    {
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
        _ = Task.Run(() => TryFetchAndMergeAsync(peer, _cts?.Token ?? CancellationToken.None));

        if (peer.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase))
            return;

        _ = Task.Run(async () =>
        {
            foreach (var streamType in Enum.GetValues<ManifestStreamType>())
            {
                var manifest = GetLocalManifest(streamType);
                if (manifest == null)
                {
                    _logger.Debug($"OnPeerAdded: No {streamType} manifest available for {peer.UserId}");
                    continue;
                }
                _logger.Debug($"OnPeerAdded: Pushing {streamType} manifest ({manifest.Operations.Count} ops) to {peer.UserId}");

                Manifest manifestToPush;
                lock (manifest)
                {
                    manifestToPush = new Manifest
                    {
                        UserId = manifest.UserId,
                        StreamType = manifest.StreamType,
                        Snapshot = manifest.Snapshot,
                        Operations = manifest.Operations.ToList(),
                        Version = manifest.Version,
                        LastUpdated = manifest.LastUpdated
                    };
                }

                try
                {
                    await _client.PushManifestAsync(peer.Address, peer.Port, manifestToPush, BuildAnnouncingPeerInfo(manifestToPush.StreamType));
                    RecordPeerMessage(peer.UserId, "PushManifest", success: true,
                        $"Pushed local {manifestToPush.StreamType} manifest ({manifestToPush.Operations.Count} op) to {peer.Address}:{peer.Port}.");
                }
                catch (Exception ex)
                {
                    RecordPeerMessage(peer.UserId, "PushManifest", success: false,
                        $"Push failed for {manifestToPush.StreamType} to {peer.Address}:{peer.Port}: {ex.Message}");
                }
            }
        });
    }

    private void OnPeerRemoved(object? sender, string userId)
    {
        PeerCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
    {
        // Ignore pushes from ourselves
        if (e.Manifest.UserId == Identity?.UserId)
        {
            _logger.Debug("Ignored manifest push from self ({0})", e.Manifest.UserId);
            return;
        }

        Interlocked.Increment(ref _inboundManifestPushCount);
        RecordPeerMessage(e.Manifest.UserId, "PushManifest", success: true,
            $"Received manifest with {e.Manifest.Operations.Count} operation(s) from {e.PeerAddress}.");

        var peer = _router.GetPeers().FirstOrDefault(p => p.UserId == e.Manifest.UserId);

        if (peer == null)
        {
            var profile = e.Manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();

            var discovered = new PeerInfo
            {
                UserId = e.Manifest.UserId,
                DisplayName = SecurityLimits.Truncate(
                    profile?.Metadata.GetValueOrDefault("displayName")
                    ?? e.AnnouncingPeer?.DisplayName
                    ?? e.Manifest.UserId,
                    SecurityLimits.MaxDisplayNameLength),
                Address = e.PeerAddress,
                Port = e.AnnouncingPeer?.Port > 0 ? e.AnnouncingPeer.Port : ManifestExchangeServer.DefaultPort,
                LastSeen = DateTime.UtcNow,
                PublicKeyPem = e.AnnouncingPeer?.PublicKeyPem
                    ?? profile?.Metadata.GetValueOrDefault("publicKeyPem")
                    ?? string.Empty
            };

            _router.LearnPeers([discovered]);
            peer = _router.GetPeers().FirstOrDefault(p => p.UserId == e.Manifest.UserId);
        }

        var publicKeyPem = peer?.PublicKeyPem;
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            publicKeyPem = e.AnnouncingPeer?.PublicKeyPem;

        if (string.IsNullOrWhiteSpace(publicKeyPem))
            publicKeyPem = e.Manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .Select(op => op.Metadata.GetValueOrDefault("publicKeyPem"))
                .FirstOrDefault(pk => !string.IsNullOrWhiteSpace(pk));

        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return;

        TryMerge(e.Manifest, publicKeyPem);
    }

    private async Task TryFetchAndMergeAsync(PeerInfo peer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peer.PublicKeyPem)) return;
        if (peer.UserId == Identity?.UserId) return;

        foreach (ManifestStreamType streamType in Enum.GetValues(typeof(ManifestStreamType)))
            try
            {
                var existing = _peerStore.Get(peer.UserId, streamType);
                var startSeq = (existing?.Snapshot?.LastSequenceNumber ?? -1) + 1 + (existing?.Operations.Count ?? 0);

                Manifest? remoteManifest = null;
                var fetchedFromPeer = false;

                try
                {
                    if (peer.Port > 0)
                    {
                        remoteManifest = await _client.FetchManifestAsync(peer.Address, peer.Port, _peerStore, peer.UserId, streamType, ct);
                        fetchedFromPeer = remoteManifest != null;
                    }
                }
                catch {
                    /* fallback to relay if peer is unreachable */
                }

                if (remoteManifest == null && peer.Capabilities.Contains("relay"))
                    foreach (var bootstrap in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
                        if (TryParseEndpoint(bootstrap, out var host, out var port))
                            try
                            {
                                remoteManifest = await _client.FetchManifestAsync(host, port, _peerStore, peer.UserId, streamType, ct);
                                if (remoteManifest != null)
                                {
                                    RecordPeerMessage(peer.UserId, "FetchManifestRelay", success: true,
                                        $"Fetched {streamType} manifest from bootstrap relay {host}:{port}.");
                                    break;
                                }
                            }
                            catch { }

                if (remoteManifest == null)
                {
                    RecordPeerMessage(peer.UserId, "FetchManifest", success: false,
                        $"Peer {peer.Address}:{peer.Port} returned no {streamType} manifest and relay fallback failed.");
                    continue;
                }

                Interlocked.Increment(ref _outboundManifestFetchCount);
                var details = $"Fetched {streamType} manifest with {remoteManifest.Operations.Count} operation(s) (delta sync from seq {startSeq}). FromPeer={fetchedFromPeer}";
                _logger.Debug(details);
                RecordPeerMessage(peer.UserId, "FetchManifest", success: true,
                    details);
                TryMerge(remoteManifest, peer.PublicKeyPem);
            }
            catch (Exception ex)
            {
                RecordPeerMessage(peer.UserId, "FetchManifest", success: false,
                    $"Fetch failed for {streamType}: {ex.Message}");
            }
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
                var peers = await _client.FetchPeersAsync(host, port, customLabel: "bootstrap");
                if (peers != null)
                {
                    _router.LearnPeers(peers);
                    refreshed = true;
                    report.Attempts.Add(new PeerConnectionAttemptResult(
                        "bootstrap-refresh",
                        true,
                        $"Fetched {peers.Count} peers from bootstrap {host}:{port}."));
                }
                else
                {
                    report.Attempts.Add(new PeerConnectionAttemptResult(
                        "bootstrap-refresh",
                        false,
                        $"Failed to reach bootstrap {host}:{port}."));
                }
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
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "bootstrap-refresh",
                false,
                "Bootstrap refresh completed without usable peer data."));
    }

    private async Task<RendezvousResponse?> RequestBootstrapRendezvousAsync(string targetUserId, PeerConnectionAttemptReport report)
    {
        if (_bootstrapNodes.Count == 0 || Identity == null)
            return null;

        foreach (var endpoint in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
        {
            if (!TryParseEndpoint(endpoint, out var host, out var port))
                continue;

            try
            {
                var response = await _client.RequestRendezvousAsync(host, port, new RendezvousRequest
                {
                    InitiatorUserId = Identity.UserId,
                    TargetUserId = targetUserId,
                    InitiatorPort = Identity.ManifestPort,
                    RequestedProbeWindowMs = 4_000
                });

                if (response != null)
                    return response;
            }
            catch (Exception ex)
            {
                report.Attempts.Add(new PeerConnectionAttemptResult(
                    "bootstrap-rendezvous",
                    false,
                    $"Rendezvous request to {host}:{port} failed: {ex.Message}"));
            }
        }

        return null;
    }

    private static async Task WaitForProbeWindowAsync(RendezvousResponse rendezvous, PeerConnectionAttemptReport report)
    {
        var now = DateTime.UtcNow;
        if (rendezvous.ProbeStartUtc <= now)
            return;

        var delay = rendezvous.ProbeStartUtc - now;
        if (delay > TimeSpan.FromSeconds(8))
        {
            report.Attempts.Add(new PeerConnectionAttemptResult(
                "bootstrap-rendezvous-timing",
                false,
                "Probe start is too far in the future; skipping wait."));
            return;
        }

        await Task.Delay(delay);
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

    private void RecordPeerMessage(string userId, string messageType, bool success, string details)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        lock (_diagnosticsLock)
        {
            if (!_peerMessageLogs.TryGetValue(userId, out var queue))
            {
                queue = new Queue<PeerMessageLogEntry>();
                _peerMessageLogs[userId] = queue;
            }

            queue.Enqueue(new PeerMessageLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                MessageType = messageType,
                Success = success,
                Details = details
            });

            while (queue.Count > MaxMessageLogEntriesPerPeer)
                queue.Dequeue();
        }
    }

    private static int CountPublishedItems(Manifest? manifest, string targetType)
    {
        if (manifest == null)
            return 0;

        return manifest.Operations
            .Where(op => string.Equals(op.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                      && (op.OperationType == ManifestOperationType.Create
                       || op.OperationType == ManifestOperationType.Update
                       || op.OperationType == ManifestOperationType.Delete))
            .GroupBy(op => op.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(op => op.SequenceNumber).First())
            .Count(op => op.OperationType != ManifestOperationType.Delete);
    }

    private static string ResolveDisplayName(Manifest? manifest, PeerInfo? peer)
    {
        var profileOp = manifest?.Operations
            .Where(op => op.OperationType == ManifestOperationType.Profile)
            .OrderByDescending(op => op.SequenceNumber)
            .FirstOrDefault();

        var profileName = profileOp?.Metadata.GetValueOrDefault("displayName");
        if (!string.IsNullOrWhiteSpace(profileName))
            return profileName;

        if (!string.IsNullOrWhiteSpace(peer?.DisplayName) && !peer.DisplayName.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase))
            return peer.DisplayName;

        if (!string.IsNullOrWhiteSpace(manifest?.UserId))
            return manifest.UserId;

        if (!string.IsNullOrWhiteSpace(peer?.UserId))
            return peer.UserId;

        if (!string.IsNullOrWhiteSpace(peer?.Address))
            return $"{peer.Address}:{peer.Port}";

        return "Unknown Peer";
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
    public bool RecordPlay(string trackId, string title, string artist, string? contentHash = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Play));
        if (manifest == null || Identity == null) return false;
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        if (!_playedThisSession.Add(trackId)) return false;   // already counted this session

        _logger.Info("Recording play for track '{0}' (ID: {1})", title, trackId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Play,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: contentHash,
            new Dictionary<string, string>
            {
                ["title"]  = SecurityLimits.Truncate(title,  SecurityLimits.MaxTrackTitleLength),
                ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength)
            },
            Identity.PrivateKeyPem);

        return true;
    }

    /// <summary>
    /// Announces a track release to the network by appending a signed Create operation to the local manifest.
    /// Automatically stamps <c>releasedAt</c> (ISO-8601 UTC) into the metadata dictionary if not already set.
    /// Call this when the user marks a track as released and wants peers to discover it.
    /// </summary>
    public void AnnounceTrack(string trackId, string contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Create));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta.TryAdd("releasedAt", DateTime.UtcNow.ToString("O"));

        var title = meta.GetValueOrDefault("title") ?? trackId;
        _logger.Info("Announcing track release: '{0}' (ID: {1}, Hash: {2})", title, trackId, contentHash);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Create,
            trackId,
            "Track",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Updates a released track in the network by appending a signed Update operation to the local manifest.
    /// </summary>
    public void UpdateTrack(string trackId, string contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Update));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];

        var title = meta.GetValueOrDefault("title") ?? trackId;
        _logger.Info("Announcing track update: '{0}' (ID: {1}, Hash: {2})", title, trackId, contentHash);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Update,
            trackId,
            "Track",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Announces an album release to the network.
    /// Automatically stamps <c>releasedAt</c> (ISO-8601 UTC) into the metadata dictionary if not already set.
    /// </summary>
    public void AnnounceAlbum(string albumId, string? contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Create));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta.TryAdd("releasedAt", DateTime.UtcNow.ToString("O"));

        var name = meta.GetValueOrDefault("name") ?? albumId;
        _logger.Info("Announcing album release: '{0}' (ID: {1})", name, albumId);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Create,
            albumId,
            "Album",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Updates a released album in the network by appending a signed Update operation to the local manifest.
    /// </summary>
    public void UpdateAlbum(string albumId, string? contentHash, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Update));
        if (manifest == null || Identity == null) return;
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];

        var name = meta.GetValueOrDefault("name") ?? albumId;
        _logger.Info("Announcing album update: '{0}' (ID: {1})", name, albumId);

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Update,
            albumId,
            "Album",
            contentHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed Follow op for <paramref name="targetUserId"/> to the local manifest.
    /// Safe to call multiple times — duplicate ops are ignored during merge.
    /// </summary>
    public void RecordFollow(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Follow));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording follow for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Follow,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed FriendAdd op for <paramref name="targetUserId"/>.
    /// </summary>
    public void RecordFriendAdd(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.FriendAdd));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording friend add for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.FriendAdd,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed FriendRemove op for <paramref name="targetUserId"/>.
    /// </summary>
    public void RecordFriendRemove(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.FriendRemove));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording friend remove for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.FriendRemove,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed GroupJoin op for <paramref name="groupId"/>.
    /// </summary>
    public void RecordGroupJoin(string groupId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.GroupJoin));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording group join for group {0}", groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.GroupJoin,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed GroupLeave op for <paramref name="groupId"/>.
    /// </summary>
    public void RecordGroupLeave(string groupId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.GroupLeave));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        _logger.Info("Recording group leave for group {0}", groupId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.GroupLeave,
            SecurityLimits.Truncate(groupId, SecurityLimits.MaxTargetIdLength),
            "Group",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed Unfollow op for <paramref name="targetUserId"/> to the local manifest.
    /// </summary>
    public void RecordUnfollow(string targetUserId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Unfollow));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(targetUserId)) return;

        _logger.Info("Recording unfollow for user {0}", targetUserId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Unfollow,
            SecurityLimits.Truncate(targetUserId, SecurityLimits.MaxTargetIdLength),
            "User",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed Comment op for a track to the local manifest.
    /// </summary>
    public string? RecordComment(string trackId, string commentText, string? replyToId = null, Dictionary<string, string>? metadata = null)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Comment));
        if (manifest == null || Identity == null) return null;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(commentText)) return null;

        _logger.Info("Recording comment for track {0}: '{1}'", trackId, SecurityLimits.Truncate(commentText, 32));
        var meta = metadata != null ? new Dictionary<string, string>(metadata) : [];
        meta["text"] = SecurityLimits.Truncate(commentText, SecurityLimits.MaxCommentTextLength);
        if (!string.IsNullOrWhiteSpace(replyToId))
            meta["replyToId"] = SecurityLimits.Truncate(replyToId, SecurityLimits.MaxOperationIdLength);

        var op = _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Comment,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: meta,
            Identity.PrivateKeyPem);

        PersistAndFanoutLocalManifest(manifest.StreamType);
        return op.OperationId;
    }

    /// <summary>
    /// Appends a signed CommentDelete op for a previously authored comment operation.
    /// </summary>
    public void RecordCommentDelete(string trackId, string commentOperationId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.CommentDelete));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(commentOperationId)) return;

        _logger.Info("Recording comment deletion for track {0}, op {1}", trackId, commentOperationId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.CommentDelete,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: new Dictionary<string, string>
            {
                ["commentOperationId"] = SecurityLimits.Truncate(commentOperationId, SecurityLimits.MaxOperationIdLength)
            },
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed Like op for a track.
    /// </summary>
    public void RecordLike(string trackId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Like));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId)) return;

        _logger.Info("Recording like for track {0}", trackId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Like,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Appends a signed Unlike op for a track.
    /// </summary>
    public void RecordUnlike(string trackId)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Unlike));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(trackId)) return;

        _logger.Info("Recording unlike for track {0}", trackId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Unlike,
            SecurityLimits.Truncate(trackId, SecurityLimits.MaxTargetIdLength),
            "Track",
            contentHash: null,
            metadata: null,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    /// <summary>
    /// Broadcasts the user's current profile as a signed Profile op.
    /// Peers receiving this op can update their local view of the user's identity.
    /// </summary>
    public void BroadcastProfile(string displayName, bool isArtist, string bio, string? website, string? bannerImageHash)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.Profile));
        if (manifest == null || Identity == null) return;

        _logger.Info("Broadcasting updated profile for {0} (isArtist: {1})", displayName, isArtist);
        var meta = new Dictionary<string, string>
        {
            ["displayName"] = SecurityLimits.Truncate(displayName, SecurityLimits.MaxArtistNameLength),
            ["isArtist"]    = isArtist.ToString(),
            ["bio"]         = SecurityLimits.Truncate(bio, 1000),
            ["website"]     = SecurityLimits.Truncate(website, 256),
            ["publicKeyPem"] = Identity.PublicKeyPem
        };
        if (!string.IsNullOrWhiteSpace(bannerImageHash))
            meta["bannerImageHash"] = bannerImageHash;

        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.Profile,
            Identity.UserId,
            "User",
            contentHash: bannerImageHash,
            meta,
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    private void TryMerge(Manifest remote, string publicKeyPem)
    {
        if (remote.UserId == Identity?.UserId) return;

        _logger.Debug("Attempting merge of manifest from peer {0} ({1} ops, stream={2})", remote.UserId, remote.Operations.Count, remote.StreamType);
        var added = _peerStore.MergeAndSave(remote, publicKeyPem, _manifestManager);
        if (added > 0)
        {
            _logger.Info("Merged manifest from peer {0}: added {1} new operations.", remote.UserId, added);
            var profileOp = remote.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();

            if (profileOp != null)
            {
                _logger.Debug("Updating profile for {0} from merged manifest", remote.UserId);
                UserRepository?.UpdateProfile(remote.UserId, profileOp.Metadata);
                var iconHash = profileOp.ContentHash;
                if (!string.IsNullOrWhiteSpace(iconHash))
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var bytes = await RequestContentAsync(remote.UserId, iconHash);
                            if (bytes != null) UserRepository?.SaveUserIcon(remote.UserId, bytes);
                        }
                        catch { }
                    });
            }

            _ = CatalogueService.IngestAsync(remote);

            // Also trigger icon/content downloads for catalogue entries
            foreach (var op in remote.Operations)
                if (op.OperationType == ManifestOperationType.Create || op.OperationType == ManifestOperationType.Update)
                {
                    var iconHash = op.Metadata.GetValueOrDefault("iconHash");
                    if (string.IsNullOrWhiteSpace(iconHash) && op.TargetType == "User")
                        iconHash = op.ContentHash;

                    if (!string.IsNullOrWhiteSpace(iconHash))
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var bytes = await RequestContentAsync(remote.UserId, iconHash);
                                if (bytes != null && UserRepository != null) UserRepository.SaveUserIcon(op.TargetId, bytes);
                            }
                            catch { }
                        });
                    else if (!string.IsNullOrWhiteSpace(op.ContentHash) && (op.Metadata.ContainsKey("isIcon") && op.Metadata["isIcon"] == "True"))
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var bytes = await RequestContentAsync(remote.UserId, op.ContentHash!);
                                if (bytes != null && UserRepository != null) UserRepository.SaveUserIcon(op.TargetId, bytes);
                            }
                            catch { }
                        });
                }

            ManifestMerged?.Invoke(this, new ManifestMergedEventArgs(remote.UserId, added));
        }
        else
        {
            _logger.Trace("Merge of manifest from peer {0} resulted in 0 new operations.", remote.UserId);
        }
    }

    private void PersistAndFanoutLocalManifest(ManifestStreamType streamType)
    {
        var manifest = GetLocalManifest(streamType);
        if (manifest == null) return;

        Manifest manifestToShare;
        lock (manifest)
        {
            if (manifest.Operations.Count >= 500 && Identity != null)
            {
                _logger.Info("Compacting local {0} manifest ({1} operations)", streamType, manifest.Operations.Count);
                _manifestManager.Compact(manifest, Identity.PrivateKeyPem, threshold: 500, keepRecent: 100);
            }

            SaveLocalManifest(manifest);

            _logger.Info("Local {0} manifest updated (ops: {1}). Initiating fan-out to peers.", streamType, manifest.Operations.Count);

            // Clone for sharing to avoid race conditions with further modifications/compactions
            manifestToShare = new Manifest
            {
                UserId = manifest.UserId,
                StreamType = manifest.StreamType,
                Snapshot = manifest.Snapshot,
                Operations = manifest.Operations.ToList(),
                Version = manifest.Version,
                LastUpdated = manifest.LastUpdated
            };
        }

        _ = CatalogueService.IngestAsync(manifestToShare);

        _ = Task.Run(async () =>
        {
            var meshPeers = _router.GetPeers().Where(p => !p.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var peer in meshPeers)
                try
                {
                    _logger.Debug("Pushing local {0} manifest to peer {1} ({2}:{3})", streamType, peer.UserId, peer.Address, peer.Port);
                    await _client.PushManifestAsync(peer.Address, peer.Port, manifestToShare, BuildAnnouncingPeerInfo(streamType));
                    RecordPeerMessage(peer.UserId, "PushManifest", success: true,
                        $"Pushed local {streamType} manifest ({manifestToShare.Operations.Count} op) to {peer.Address}:{peer.Port}.");
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to push {0} manifest to {1}: {2}", streamType, peer.UserId, ex.Message);
                    RecordPeerMessage(peer.UserId, "PushManifest", success: false,
                        $"Push failed for {streamType} to {peer.Address}:{peer.Port}: {ex.Message}");
                    // best-effort push; periodic sync/merge will reconcile later
                }

            if (!_actAsListener)
                foreach (var bootstrap in _bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes))
                    if (TryParseEndpoint(bootstrap, out var host, out var port))
                        try
                        {
                            _logger.Debug("Relaying local {0} manifest via bootstrap {1}:{2}", streamType, host, port);
                            await _client.RelayManifestPushAsync(host, port, manifestToShare, BuildAnnouncingPeerInfo(streamType));
                            RecordPeerMessage($"bootstrap:{host}:{port}", "RelayManifestPush", success: true,
                                $"Pushed local {streamType} manifest to bootstrap for relaying.");
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn("Failed to relay {0} manifest via bootstrap {1}:{2}: {3}", streamType, host, port, ex.Message);
                            RecordPeerMessage($"bootstrap:{host}:{port}", "RelayManifestPush", success: false,
                                $"Relay push failed for {streamType}: {ex.Message}");
                        }
        });
    }

    private PeerInfo BuildAnnouncingPeerInfo(ManifestStreamType streamType)
    {
        var manifest = GetLocalManifest(streamType);
        return new PeerInfo
        {
            UserId = Identity?.UserId ?? manifest?.UserId ?? string.Empty,
            DisplayName = SecurityLimits.Truncate(Identity?.DisplayName ?? manifest?.UserId ?? "peer", SecurityLimits.MaxDisplayNameLength),
            Address = ExternalIPAddress ?? string.Empty,
            Port = _actAsListener ? (Identity?.ManifestPort ?? ManifestExchangeServer.DefaultPort) : 0,
            PublicKeyPem = Identity?.PublicKeyPem ?? string.Empty,
            LastSeen = DateTime.UtcNow
        };
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

public sealed class PeerMessageLogEntry
{
    public DateTime TimestampUtc { get; init; }
    public string MessageType { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Details { get; init; } = string.Empty;
}

public sealed class PeerDiagnosticsSnapshot
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool IsOnline { get; init; }
    public bool IsBootstrap { get; init; }
    public bool HasManifest { get; init; }
    public int PublishedTrackCount { get; init; }
    public int PublishedAlbumCount { get; init; }
    public int OperationCount { get; init; }
    public IReadOnlyList<PeerMessageLogEntry> RecentMessages { get; init; } = [];
}
