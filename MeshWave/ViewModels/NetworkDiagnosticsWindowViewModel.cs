using System.Collections.ObjectModel;
using System.Text;
using MeshWave.Mvvm;
using MeshWave.Synchronizer;

namespace MeshWave.ViewModels;

public sealed class NetworkDiagnosticsWindowViewModel : ViewModelBase
{
    private readonly SyncOrchestrator _sync;
    private readonly ApplicationViewModel _application;
    private string _summaryText = string.Empty;
    private string _selectedPeerLogText = "Select a peer to view the latest messages.";
    private PeerDiagnosticsItemViewModel? _selectedPeer;

    public NetworkDiagnosticsWindowViewModel(SyncOrchestrator sync, ApplicationViewModel application)
    {
        _sync = sync;
        _application = application;

        RefreshCommand = new RelayCommand(_ => Refresh());
        Peers = [];

        Refresh();
    }

    public RelayCommand RefreshCommand { get; }
    public ObservableCollection<PeerDiagnosticsItemViewModel> Peers { get; }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public PeerDiagnosticsItemViewModel? SelectedPeer
    {
        get => _selectedPeer;
        set
        {
            if (!SetProperty(ref _selectedPeer, value))
                return;

            SelectedPeerLogText = BuildPeerLogText(value);
        }
    }

    public string SelectedPeerLogText
    {
        get => _selectedPeerLogText;
        private set => SetProperty(ref _selectedPeerLogText, value);
    }

    public void Refresh()
    {
        var snapshots = _sync.GetPeerDiagnosticsSnapshots();

        Peers.Clear();
        foreach (var snapshot in snapshots)
        {
            var userId = !string.IsNullOrWhiteSpace(snapshot.UserId) ? snapshot.UserId : "(unknown id)";
            var displayName = !string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.DisplayName : userId;

            Peers.Add(new PeerDiagnosticsItemViewModel(new PeerDiagnosticsSnapshot
            {
                UserId = userId,
                DisplayName = displayName,
                Address = snapshot.Address,
                Port = snapshot.Port,
                IsOnline = snapshot.IsOnline,
                IsBootstrap = snapshot.IsBootstrap,
                HasManifest = snapshot.HasManifest,
                PublishedTrackCount = snapshot.PublishedTrackCount,
                PublishedAlbumCount = snapshot.PublishedAlbumCount,
                OperationCount = snapshot.OperationCount,
                RecentMessages = snapshot.RecentMessages
            }));
        }

        var deduped = Peers
            .GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.IsOnline).ThenByDescending(p => p.HasManifest).First())
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.IsBootstrapPeer)
            .ThenByDescending(p => p.PublishedTrackCount)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Peers.Clear();
        foreach (var item in deduped)
            Peers.Add(item);

        if (Peers.Count == 0)
        {
            foreach (var routedPeer in _sync.GetPeers())
            {
                var userId = !string.IsNullOrWhiteSpace(routedPeer.UserId) ? routedPeer.UserId : "(unknown id)";
                var existing = Peers.Any(p => string.Equals(p.UserId, userId, StringComparison.OrdinalIgnoreCase));
                if (existing)
                    continue;

                Peers.Add(new PeerDiagnosticsItemViewModel(new PeerDiagnosticsSnapshot
                {
                    UserId = userId,
                    DisplayName = !string.IsNullOrWhiteSpace(routedPeer.DisplayName) ? routedPeer.DisplayName : userId,
                    Address = routedPeer.Address,
                    Port = routedPeer.Port,
                    IsOnline = true,
                    IsBootstrap = routedPeer.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase),
                    HasManifest = false,
                    PublishedTrackCount = 0,
                    PublishedAlbumCount = 0,
                    OperationCount = 0,
                    RecentMessages = []
                }));
            }
        }

        var localTracks = _sync.LocalPublishedTrackCount;
        var localAlbums = _sync.LocalPublishedAlbumCount;
        var localOps = _sync.LocalManifest?.Operations.Count ?? 0;

        var routingPeers = _sync.GetPeers().ToList();
        var meshRoutingPeers = routingPeers.Count(p => !p.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase));
        var bootstrapRoutingPeers = routingPeers.Count - meshRoutingPeers;

        var diagnosticsMeshPeers = Peers.Count(p => !p.IsBootstrapPeer);
        var diagnosticsMeshOnline = Peers.Count(p => !p.IsBootstrapPeer && p.IsOnline);
        var diagnosticsMeshWithManifest = Peers.Count(p => !p.IsBootstrapPeer && p.HasManifest);
        var diagnosticsMeshWithoutManifest = diagnosticsMeshPeers - diagnosticsMeshWithManifest;

        var peerTracks = Peers.Where(p => !p.IsBootstrapPeer).Sum(p => p.PublishedTrackCount);
        var peerAlbums = Peers.Where(p => !p.IsBootstrapPeer).Sum(p => p.PublishedAlbumCount);

        SummaryText = $"Routing table: {_sync.ConnectedPeerCount} total ({meshRoutingPeers} mesh + {bootstrapRoutingPeers} bootstrap){Environment.NewLine}"
                    + $"Diagnostics peers: {diagnosticsMeshPeers} mesh ({diagnosticsMeshOnline} online, {diagnosticsMeshWithManifest} with manifest, {diagnosticsMeshWithoutManifest} without manifest){Environment.NewLine}"
                    + $"Exchange counters: inbound pushes={_sync.InboundManifestPushCount}, outbound fetches={_sync.OutboundManifestFetchCount}{Environment.NewLine}"
                    + $"Local published: {localAlbums} albums, {localTracks} tracks ({localOps} manifest ops){Environment.NewLine}"
                    + $"Known peer published totals: {peerAlbums} albums, {peerTracks} tracks";

        if (SelectedPeer != null)
        {
            SelectedPeer = Peers.FirstOrDefault(p => string.Equals(p.UserId, SelectedPeer.UserId, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedPeer == null && Peers.Count > 0)
        {
            SelectedPeer = Peers[0];
        }
        else if (SelectedPeer == null)
        {
            SelectedPeerLogText = "No peer diagnostics available yet.";
        }
    }

    private static string BuildPeerLogText(PeerDiagnosticsItemViewModel? peer)
    {
        if (peer == null)
            return "Select a peer to view the latest messages.";

        if (peer.Messages.Count == 0)
            return "No exchange messages recorded for this peer yet.";

        var sb = new StringBuilder();
        foreach (var entry in peer.Messages.OrderByDescending(m => m.TimestampUtc))
        {
            sb.Append('[').Append(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss")).Append("] ");
            sb.Append(entry.MessageType).Append(" · ").Append(entry.Success ? "ok" : "fail").AppendLine();
            sb.AppendLine(entry.Details);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}

public sealed class PeerDiagnosticsItemViewModel
{
    public PeerDiagnosticsItemViewModel(PeerDiagnosticsSnapshot snapshot)
    {
        UserId = snapshot.UserId;
        DisplayName = snapshot.DisplayName;
        Address = snapshot.Address;
        Port = snapshot.Port;
        IsOnline = snapshot.IsOnline;
        IsBootstrapPeer = snapshot.IsBootstrap;
        HasManifest = snapshot.HasManifest;
        PublishedTrackCount = snapshot.PublishedTrackCount;
        PublishedAlbumCount = snapshot.PublishedAlbumCount;
        OperationCount = snapshot.OperationCount;
        Messages = snapshot.RecentMessages
            .Select(m => new PeerMessageLogItemViewModel
            {
                TimestampUtc = m.TimestampUtc,
                MessageType = m.MessageType,
                Success = m.Success,
                Details = m.Details
            })
            .ToList();
    }

    public string UserId { get; }
    public string DisplayName { get; }
    public string Address { get; }
    public int Port { get; }
    public bool IsOnline { get; }
    public bool IsBootstrapPeer { get; }
    public bool HasManifest { get; }
    public int PublishedTrackCount { get; }
    public int PublishedAlbumCount { get; }
    public int OperationCount { get; }
    public IReadOnlyList<PeerMessageLogItemViewModel> Messages { get; }

    public string Endpoint => string.IsNullOrWhiteSpace(Address) ? "(unknown endpoint)" : $"{Address}:{Port}";
    public string OnlineLabel => IsOnline ? "Online" : "Offline";
    public string PeerTypeLabel => IsBootstrapPeer ? "Bootstrap" : "Mesh";
    public string ManifestLabel => HasManifest ? "Manifest: yes" : "Manifest: no";
    public string PublishedSummary => $"{PublishedAlbumCount} albums · {PublishedTrackCount} tracks · {OperationCount} ops";
}

public sealed class PeerMessageLogItemViewModel
{
    public DateTime TimestampUtc { get; init; }
    public string MessageType { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Details { get; init; } = string.Empty;
}
