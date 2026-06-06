using Mono.Nat;
using Mono.Nat.Logging;
using NLog;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Logger = NLog.Logger;

namespace MeshWave.Synchronizer;

/// <summary>
/// Lightweight UDP NAT traversal helper for peer-to-peer hole punching.
/// Both peers periodically send punch probes so NAT mappings can open in both directions.
/// Also handles automated UPnP/NAT-PMP port mapping via Mono.Nat.
/// </summary>
public sealed class NatTraversalService : IDisposable
{
    private readonly Logger _logger;
    private const string PunchPrefix = "meshwave:punch:";
    private const string AckPrefix = "meshwave:ack:";

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingPunches = new(StringComparer.OrdinalIgnoreCase);

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    private INatDevice? _natDevice;
    private Mapping? _tcpMapping;
    private Mapping? _udpMapping;
    private string? _externalIp;
    private string _natStatus = "Not attempted";

    public bool IsRunning => _udp != null;
    public string? ExternalIPAddress => _externalIp;
    public string NatStatus => _natStatus;
    public string? MappingProtocol => _natDevice?.NatProtocol.ToString();

    public NatTraversalService(Logger? logger)
    {
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(int localPort, CancellationToken cancellationToken = default)
    {
        if (_udp != null)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _udp = new UdpClient(localPort)
            {
                EnableBroadcast = false
            };
        }
        catch (SocketException)
        {
            // Do not fail overall mesh startup if UDP bind on the preferred port is unavailable.
            // Fall back to an ephemeral UDP port so NAT probing remains best-effort.
            _udp = new UdpClient(0)
            {
                EnableBroadcast = false
            };
        }

        _receiveTask = ReceiveLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _udp?.Close();

        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { }
        }

        await RemovePortMappingsAsync();

        _udp?.Dispose();
        _udp = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
        _pendingPunches.Clear();
    }

    public async Task SetupPortMappingAsync(int port, CancellationToken cancellationToken = default)
    {
        _natStatus = "Discovering NAT devices...";
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
                _externalIp = (await _natDevice.GetExternalIPAsync()).ToString();

                _logger.Info("Found NAT device: {0} ({1}). External IP: {2}",
                    _natDevice.NatProtocol, _natDevice.DeviceEndpoint, _externalIp);

                _tcpMapping = new Mapping(Protocol.Tcp, port, port, 0, "MeshWave P2P (TCP)");
                _udpMapping = new Mapping(Protocol.Udp, port, port, 0, "MeshWave P2P (UDP)");

                try
                {
                    await _natDevice.CreatePortMapAsync(_tcpMapping);
                    _logger.Info("Successfully mapped TCP port {0} via {1}", port, _natDevice.NatProtocol);
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to map TCP port {0}: {1}", port, ex.Message);
                }

                try
                {
                    await _natDevice.CreatePortMapAsync(_udpMapping);
                    _logger.Info("Successfully mapped UDP port {0} via {1}", port, _natDevice.NatProtocol);
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to map UDP port {0}: {1}", port, ex.Message);
                }

                _natStatus = $"Mapped via {_natDevice.NatProtocol}";
            }
            else
            {
                _natStatus = "No NAT device discovered (UPnP/NAT-PMP may be disabled)";
                _logger.Info(_natStatus);
            }
        }
        catch (OperationCanceledException)
        {
            _natStatus = "NAT discovery canceled";
        }
        catch (Exception ex)
        {
            _natStatus = $"NAT error: {ex.Message}";
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
            _externalIp = null;
            _natStatus = "Mappings removed";
        }
    }

    /// <summary>
    /// Sends UDP punch probes to the target and waits briefly for an ACK.
    /// Returns true when at least one ACK is received.
    /// </summary>
    public async Task<bool> TryPunchAsync(string peerAddress, int peerPort, CancellationToken cancellationToken = default)
    {
        if (_udp == null || string.IsNullOrWhiteSpace(peerAddress) || peerPort <= 0)
            return false;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(3));

        var nonce = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingPunches[nonce] = tcs;

        try
        {
            var payload = Encoding.UTF8.GetBytes(PunchPrefix + nonce);
            var endpoint = new IPEndPoint(IPAddress.Parse(peerAddress), peerPort);

            for (var i = 0; i < 8 && !linkedCts.IsCancellationRequested; i++)
            {
                await _udp.SendAsync(payload, endpoint, linkedCts.Token);
                if (tcs.Task.IsCompleted)
                    break;

                await Task.Delay(250, linkedCts.Token);
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
            return completed == tcs.Task && tcs.Task.Result;
        }
        catch
        {
            return false;
        }
        finally
        {
            _pendingPunches.TryRemove(nonce, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_udp == null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(cancellationToken);
                var text = Encoding.UTF8.GetString(result.Buffer);

                if (text.StartsWith(PunchPrefix, StringComparison.Ordinal))
                {
                    var nonce = text[PunchPrefix.Length..];
                    if (!string.IsNullOrWhiteSpace(nonce))
                    {
                        var ack = Encoding.UTF8.GetBytes(AckPrefix + nonce);
                        await _udp.SendAsync(ack, result.RemoteEndPoint, cancellationToken);
                    }
                }
                else if (text.StartsWith(AckPrefix, StringComparison.Ordinal))
                {
                    var nonce = text[AckPrefix.Length..];
                    if (_pendingPunches.TryGetValue(nonce, out var pending))
                        pending.TrySetResult(true);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // best-effort probing, ignore transient network errors
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _cts?.Dispose();
    }
}
