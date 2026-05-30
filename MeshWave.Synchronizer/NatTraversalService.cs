using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MeshWave.Synchronizer;

/// <summary>
/// Lightweight UDP NAT traversal helper for peer-to-peer hole punching.
/// Both peers periodically send punch probes so NAT mappings can open in both directions.
/// </summary>
public sealed class NatTraversalService : IDisposable
{
    private const string PunchPrefix = "meshwave:punch:";
    private const string AckPrefix = "meshwave:ack:";

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingPunches = new(StringComparer.OrdinalIgnoreCase);

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public bool IsRunning => _udp != null;

    public async Task StartAsync(int localPort, CancellationToken cancellationToken = default)
    {
        if (_udp != null)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _udp = new UdpClient(localPort)
        {
            EnableBroadcast = false
        };

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

        _udp?.Dispose();
        _udp = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
        _pendingPunches.Clear();
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
