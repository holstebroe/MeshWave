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

    }
