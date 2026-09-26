using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Storage;
using MeshWave.Common.Core.Validation;
using NLog;

namespace MeshWave.Synchronizer;

public partial class SyncOrchestrator
{
    /// <summary>
    /// Runs anti-entropy (heads exchange, then pulls of the streams where we are behind) with every peer we can reach.
    /// </summary>
    public async Task SyncAllPeersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _router.GetPeers().Where(p => !IsBootstrapEntry(p)).ToList())
            await SyncWithPeerAsync(peer, cancellationToken);
    }

    /// <summary>
    /// Anti-entropy with a bounded set of neighbours: every peer with a session plus a few random dialable ones. Streams
    /// are replicated by every peer, so a few neighbours are enough to catch up on everything a push missed.
    /// </summary>
    private async Task SyncNeighboursAsync(CancellationToken cancellationToken)
    {
        var peers = _router.GetPeers().Where(p => !IsBootstrapEntry(p) && HasRoute(p)).OrderBy(_ => Random.Shared.Next()).ToList();
        var neighbours = peers.Where(p => _sessions.GetSession(p.UserId) != null)
            .Concat(peers.Where(p => _sessions.GetSession(p.UserId) == null).Take(SecurityLimits.GossipFanout));
        foreach (var peer in neighbours)
            await SyncWithPeerAsync(peer, cancellationToken);
    }

    /// <summary>
    /// Periodic anti-entropy. Updates normally arrive as pushes; this repairs anything a push missed.
    /// </summary>
    private async Task PeriodicSyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(SecurityLimits.PeriodicSyncIntervalSeconds), ct);
                await SyncNeighboursAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.Debug("Periodic sync cycle failed: {0}", ex.Message);
            }
    }

}
