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
public partial class SyncOrchestrator : ISyncBrowseClient, IDisposable
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
    private Competitions.CompetitionTallyService? _tallyService;

    private readonly Lock _diagnosticsLock = new();
    private readonly Dictionary<string, Queue<PeerMessageLogEntry>> _peerMessageLogs = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxMessageLogEntriesPerPeer = 100;

    // Tracks which trackIds have already had a Play op recorded in this process session.
    private readonly HashSet<string> _playedThisSession = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ManifestMergedEventArgs>? ManifestMerged;
    public event EventHandler? PeerCountChanged;
    public event EventHandler<GroupMessageEventArgs>? GroupMessageReceived;
    public event EventHandler<GroupStateChangedEventArgs>? GroupStateChanged;

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

    public NatTraversalService NatTraversal => _natTraversal;

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

        _tallyService = new Competitions.CompetitionTallyService(this, _peerStore, _logger);
        _tallyService.Start();

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
        if (_tallyService != null)
            await _tallyService.StopAsync();
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


    /// <summary>
    /// Saves all local manifests to disk.
    /// </summary>




    /// <summary>
    /// Compatibility method for saving local manifest.
    /// </summary>


    /// <summary>
    /// Loads a previously persisted local manifest for the given userId and stream.
    /// Returns null if no persisted manifest exists.
    /// </summary>




    /// <summary>
    /// Manually triggers a manifest fetch from all known peers.
    /// </summary>


    /// <summary>
    /// Requests content bytes from a currently known peer by content hash.
    /// </summary>































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


    /// <summary>
    /// Announces a track release to the network by appending a signed Create operation to the local manifest.
    /// Automatically stamps <c>releasedAt</c> (ISO-8601 UTC) into the metadata dictionary if not already set.
    /// Call this when the user marks a track as released and wants peers to discover it.
    /// </summary>


    /// <summary>
    /// Updates a released track in the network by appending a signed Update operation to the local manifest.
    /// </summary>


    /// <summary>
    /// Announces an album release to the network.
    /// Automatically stamps <c>releasedAt</c> (ISO-8601 UTC) into the metadata dictionary if not already set.
    /// </summary>


    /// <summary>
    /// Updates a released album in the network by appending a signed Update operation to the local manifest.
    /// </summary>


    /// <summary>
    /// Appends a signed Follow op for <paramref name="targetUserId"/> to the local manifest.
    /// Safe to call multiple times — duplicate ops are ignored during merge.
    /// </summary>


    /// <summary>
    /// Appends a signed FriendAdd op for <paramref name="targetUserId"/>.
    /// </summary>


    /// <summary>
    /// Appends a signed FriendRemove op for <paramref name="targetUserId"/>.
    /// </summary>


    /// <summary>
    /// Appends a signed FoundGroup op for <paramref name="groupId"/>.
    /// </summary>


    /// <summary>
    /// Appends a signed ModerateGroup op for <paramref name="groupId"/>.
    /// </summary>


    /// <summary>
    /// Appends a signed CreateChannel op.
    /// </summary>


    /// <summary>
    /// Appends a signed PostMessage op.
    /// </summary>


    /// <summary>
    /// Appends a signed GroupJoin op for <paramref name="groupId"/>.
    /// </summary>


    /// <summary>
    /// Appends a signed GroupLeave op for <paramref name="groupId"/>.
    /// </summary>


    /// <summary>
    /// Appends a signed Unfollow op for <paramref name="targetUserId"/> to the local manifest.
    /// </summary>


    /// <summary>
    /// Appends a signed Comment op for a track to the local manifest.
    /// </summary>


    /// <summary>
    /// Appends a signed CommentDelete op for a previously authored comment operation.
    /// </summary>


    /// <summary>
    /// Appends a signed Like op for a track.
    /// </summary>


    /// <summary>
    /// Appends a signed Unlike op for a track.
    /// </summary>


    /// <summary>
    /// Broadcasts the user's current profile as a signed Profile op.
    /// Peers receiving this op can update their local view of the user's identity.
    /// </summary>






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

    /// <summary>
    /// Appends a signed CompetitionRevealResults op for <paramref name="compId"/>.
    /// </summary>
    public void RecordCompetitionRevealResults(string compId, string resultJson)
    {
        var manifest = GetLocalManifest(ManifestStreamMapper.GetStreamType(ManifestOperationType.CompetitionRevealResults));
        if (manifest == null || Identity == null) return;
        if (string.IsNullOrWhiteSpace(compId)) return;

        _logger.Info("Recording competition reveal results for competition {0}", compId);
        _manifestManager.AppendSignedOperation(
            manifest,
            ManifestOperationType.CompetitionRevealResults,
            SecurityLimits.Truncate(compId, SecurityLimits.MaxTargetIdLength),
            "Competition",
            contentHash: null,
            metadata: new Dictionary<string, string> { { "ResultPayload", resultJson } },
            Identity.PrivateKeyPem);
        PersistAndFanoutLocalManifest(manifest.StreamType);
    }

    public void Dispose()
    {
        _router.Dispose();
        _server?.Dispose();
        _natTraversal.Dispose();
        _cts?.Dispose();
    }
}
