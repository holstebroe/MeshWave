using Mono.Nat;
using NLog;
using Logger = NLog.Logger;

namespace MeshWave.Synchronizer;

/// <summary>
/// Automated UPnP/NAT-PMP port mapping via Mono.Nat.
/// Hole punching lives in <see cref="UdpSessionHost"/>; this service only opens ports on the local router
/// and reports the mapped external address and port.
/// </summary>
public sealed class NatTraversalService : IDisposable
{
    private readonly Logger _logger;

    private INatDevice? _natDevice;
    private Mapping? _tcpMapping;
    private Mapping? _udpMapping;

    public string? ExternalIPAddress { get; private set; }

    /// <summary>
    /// The public TCP port the router forwards to our listener, or null if no TCP mapping exists.
    /// May differ from the local port when the router picked another one.
    /// </summary>
    public int? ExternalPort { get; private set; }

    public string NatStatus { get; private set; } = "Not attempted";
    public string Diagnostics { get; private set; } = "Initializing...";

    public string? MappingProtocol => _natDevice?.NatProtocol.ToString();

    public NatTraversalService(Logger? logger)
    {
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public async Task StopAsync()
    {
        await RemovePortMappingsAsync();
    }

    public async Task SetupPortMappingAsync(int port, CancellationToken cancellationToken = default)
    {
        NatStatus = "Discovering NAT devices...";
        Diagnostics = "Sending UPnP/PMP discovery broadcast to local network...";
        _logger.Info("Starting NAT discovery for port {0} (TCP/UDP)", port);

        var tcs = new TaskCompletionSource<INatDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        EventHandler<DeviceEventArgs> handler = (s, e) =>
        {
            tcs.TrySetResult(e.Device);
        };

        NatUtility.DeviceFound += handler;
        try
        {
            NatUtility.StartDiscovery();

            // Wait for a device to be found, or timeout
            var discoveryTask = tcs.Task;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            var completedTask = await Task.WhenAny(discoveryTask, timeoutTask);
            if (completedTask == discoveryTask)
            {
                _natDevice = await discoveryTask;
                ExternalIPAddress = (await _natDevice.GetExternalIPAsync()).ToString();

                _logger.Info("Found NAT device: {0} ({1}). External IP: {2}",
                    _natDevice.NatProtocol, _natDevice.DeviceEndpoint, ExternalIPAddress);

                _tcpMapping = new Mapping(Protocol.Tcp, port, port, 0, "MeshWave P2P (TCP)");
                _udpMapping = new Mapping(Protocol.Udp, port, port, 0, "MeshWave P2P (UDP)");

                try
                {
                    // The router may assign a different public port than the one requested; announce what it created.
                    var created = await _natDevice.CreatePortMapAsync(_tcpMapping);
                    _tcpMapping = created ?? _tcpMapping;
                    ExternalPort = _tcpMapping.PublicPort > 0 ? _tcpMapping.PublicPort : port;
                    _logger.Info("Successfully mapped TCP port {0} -> {1} via {2}", ExternalPort, port, _natDevice.NatProtocol);
                }
                catch (Exception ex)
                {
                    _tcpMapping = null;
                    ExternalPort = null;
                    _logger.Warn("Failed to map TCP port {0}: {1}", port, ex.Message);
                }

                try
                {
                    var created = await _natDevice.CreatePortMapAsync(_udpMapping);
                    _udpMapping = created ?? _udpMapping;
                    _logger.Info("Successfully mapped UDP port {0} -> {1} via {2}", _udpMapping.PublicPort, port, _natDevice.NatProtocol);
                }
                catch (Exception ex)
                {
                    _udpMapping = null;
                    _logger.Warn("Failed to map UDP port {0}: {1}", port, ex.Message);
                }

                if (ExternalPort != null)
                {
                    NatStatus = $"Mapped via {_natDevice.NatProtocol}";
                    Diagnostics = $"UPnP/PMP configuration successful. Router external IP: {ExternalIPAddress}. Mapped TCP port {ExternalPort} to local port {port}.";
                }
                else
                {
                    NatStatus = $"Port mapping refused by {_natDevice.NatProtocol} device";
                    Diagnostics = $"A {_natDevice.NatProtocol} device was found (external IP {ExternalIPAddress}) but refused to map TCP port {port}. The bootstrap's dial-back check decides whether this peer is reachable; if not, it runs outbound-only.";
                }
            }
            else
            {
                NatStatus = "No NAT device discovered (UPnP/NAT-PMP may be disabled)";
                Diagnostics = "UPnP Discovery failed: Router may not support UPnP, or it is disabled. You are likely behind a Symmetric NAT or strict firewall. Manual port forwarding is recommended.";
                _logger.Info(NatStatus);
            }
        }
        catch (OperationCanceledException)
        {
            NatStatus = "NAT discovery canceled";
            Diagnostics = "Discovery was aborted.";
        }
        catch (Exception ex)
        {
            NatStatus = $"NAT error: {ex.Message}";
            Diagnostics = $"An error occurred during NAT traversal: {ex.Message}";
            _logger.Warn("NAT mapping error: {0}", ex.Message);
        }
        finally
        {
            NatUtility.StopDiscovery();
            NatUtility.DeviceFound -= handler;
        }
    }

    private async Task RemovePortMappingsAsync()
    {
        if (_natDevice == null) return;

        try
        {
            if (_tcpMapping != null)
            {
                await _natDevice.DeletePortMapAsync(_tcpMapping);
                _logger.Info("Removed TCP port mapping for {0}", _tcpMapping.PublicPort);
            }
            if (_udpMapping != null)
            {
                await _natDevice.DeletePortMapAsync(_udpMapping);
                _logger.Info("Removed UDP port mapping for {0}", _udpMapping.PublicPort);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("Error removing port mappings: {0}", ex.Message);
        }
        finally
        {
            _tcpMapping = null;
            _udpMapping = null;
            _natDevice = null;
            ExternalIPAddress = null;
            ExternalPort = null;
            NatStatus = "Mappings removed";
        }
    }

    public void Dispose()
    {
    }
}
