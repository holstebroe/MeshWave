using MeshWave.Common.Core.P2P;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MeshWave.Synchronizer;

/// <summary>
/// PeerDiscovery handles discovery of peers on the local network using UDP broadcast.
/// Peers announce themselves with identity info; listeners maintain a live peer table.
/// </summary>
public class PeerDiscovery(int listenPort = PeerDiscovery.DefaultDiscoveryPort) : IDisposable
{
    public const int DefaultDiscoveryPort = 39876;
    private const int AnnouncePeriodMs = 10_000;
    private const int PeerTimeoutMs = 60_000;

    private readonly Dictionary<string, PeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _peersLock = new();
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _announceTask;
    private LocalPeerIdentity? _identity;

    public event EventHandler<PeerInfo>? PeerDiscovered;

    /// <summary>
    /// Starts listening for peer announcements and broadcasting this peer's own presence.
    /// </summary>
    public virtual async Task StartDiscoveryAsync(LocalPeerIdentity identity, CancellationToken cancellationToken = default)
    {
        _identity = identity;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
        _udpClient.EnableBroadcast = true;

        _listenTask = ListenLoopAsync(_cts.Token);
        _announceTask = AnnounceLoopAsync(_cts.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops discovery and announcement.
    /// </summary>
    public virtual async Task StopDiscoveryAsync()
    {
        _cts?.Cancel();
        _udpClient?.Close();

        if (_listenTask != null)
        {
            try { await _listenTask; } catch { }
        }
        if (_announceTask != null)
        {
            try { await _announceTask; } catch { }
        }

        _udpClient?.Dispose();
        _udpClient = null;
    }

    /// <summary>
    /// Gets currently known live peers (seen within the timeout window).
    /// </summary>
    public IEnumerable<PeerInfo> GetDiscoveredPeers()
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-PeerTimeoutMs);
        lock (_peersLock)
        {
            return _peers.Values
                .Where(p => p.LastSeen >= cutoff)
                .OrderBy(p => p.DisplayName)
                .ToList();
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        if (_identity == null || _udpClient == null) return;

        var announcement = BuildAnnouncement(_identity);
        var endpoint = new IPEndPoint(IPAddress.Broadcast, listenPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var payload = Encoding.UTF8.GetBytes(announcement);
                await _udpClient.SendAsync(payload, endpoint, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore transient send errors */ }

            try
            {
                await Task.Delay(AnnouncePeriodMs, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        if (_udpClient == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                var json = Encoding.UTF8.GetString(result.Buffer);
                ProcessAnnouncement(json, result.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore malformed packets */ }
        }
    }

    private void ProcessAnnouncement(string json, string sourceAddress)
    {
        try
        {
            var announcement = JsonSerializer.Deserialize<PeerAnnouncement>(json);
            if (announcement == null || string.IsNullOrWhiteSpace(announcement.UserId))
            {
                return;
            }

            if (string.Equals(announcement.UserId, _identity?.UserId, StringComparison.OrdinalIgnoreCase))
            {
                return; // ignore own announcements
            }

            lock (_peersLock)
            {
                if (!_peers.TryGetValue(announcement.UserId, out var existing))
                {
                    existing = new PeerInfo
                    {
                        UserId = announcement.UserId,
                        DisplayName = announcement.DisplayName,
                        Address = sourceAddress,
                        Port = announcement.ManifestPort,
                        PublicKeyPem = announcement.PublicKeyPem,
                        Capabilities = announcement.Capabilities,
                        LastSeen = DateTime.UtcNow
                    };
                    _peers[announcement.UserId] = existing;
                    PeerDiscovered?.Invoke(this, existing);
                }
                else
                {
                    existing.LastSeen = DateTime.UtcNow;
                    existing.Address = sourceAddress;
                    existing.Port = announcement.ManifestPort;
                    existing.DisplayName = announcement.DisplayName;
                }
            }
        }
        catch { /* ignore parse errors */ }
    }

    private static string BuildAnnouncement(LocalPeerIdentity identity)
    {
        var announcement = new PeerAnnouncement
        {
            UserId = identity.UserId,
            DisplayName = identity.DisplayName,
            ManifestPort = identity.ManifestPort,
            PublicKeyPem = identity.PublicKeyPem,
            Capabilities = ["manifest-exchange", "content-exchange"]
        };
        return JsonSerializer.Serialize(announcement);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>
/// Identifies this local peer for announcements.
/// </summary>
public class LocalPeerIdentity
{
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string PublicKeyPem { get; set; }
    public required string PrivateKeyPem { get; set; }
    public int ManifestPort { get; set; } = ManifestExchangeServer.DefaultPort;
}

/// <summary>
/// UDP discovery announcement payload.
/// </summary>
internal class PeerAnnouncement
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ManifestPort { get; set; }
    public string PublicKeyPem { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = [];
}
