using System.Text.Json;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using NLog;

namespace MeshWave.Synchronizer.Competitions;

/// <summary>
/// Background service that periodically tallies votes for expired competitions
/// authored by the local user and publishes the results.
/// </summary>
public class CompetitionTallyService(
    SyncOrchestrator syncOrchestrator,
    IManifestStore peerStore,
    ILogger logger)
{
    private CancellationTokenSource? _cts;
    private Task? _tallyTask;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public CompetitionTallyService(SyncOrchestrator syncOrchestrator, IManifestStore peerStore)
        : this(syncOrchestrator, peerStore, LogManager.GetCurrentClassLogger())
    {
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _tallyTask = Task.Run(() => TallyLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_tallyTask != null)
            {
                try
                {
                    await _tallyTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task TallyLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredCompetitionsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in CompetitionTallyService loop.");
            }

            try
            {
                await Task.Delay(_checkInterval, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessExpiredCompetitionsAsync(CancellationToken cancellationToken)
    {
        if (syncOrchestrator.Identity == null) return;
        var privateKeyPem = syncOrchestrator.Identity.PrivateKeyPem;

        var localSocialManifest = syncOrchestrator.GetLocalManifest(ManifestStreamType.Social);
        if (localSocialManifest == null) return;

        List<ManifestOperation> localCompetitions;
        List<ManifestOperation> localReveals;

        lock (localSocialManifest)
        {
            localCompetitions = localSocialManifest.Operations
                .Where(o => o.OperationType == ManifestOperationType.CreateCompetition)
                .ToList();
            localReveals = localSocialManifest.Operations
                .Where(o => o.OperationType == ManifestOperationType.CompetitionRevealResults)
                .ToList();
        }

        foreach (var compOp in localCompetitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compId = compOp.TargetId;
            if (string.IsNullOrEmpty(compId)) continue;

            if (!compOp.Metadata.TryGetValue("VotingDeadline", out var deadlineStr) ||
                !DateTime.TryParse(deadlineStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var votingDeadline))
            {
                continue;
            }

            if (DateTime.UtcNow <= votingDeadline) continue;

            // Check if already tallied
            if (localReveals.Any(r => r.TargetId == compId)) continue;

            logger.Info("Tallying votes for expired competition {0}", compId);
            await TallyCompetitionAsync(compId, privateKeyPem, localSocialManifest);
        }
    }

    private Task TallyCompetitionAsync(string compId, string privateKeyPem, Manifest localSocialManifest)
    {
        // 1. Gather all votes for this competition from all peer manifests (and local)
        var allVotes = new List<ManifestOperation>();

        // From peers
        foreach (var peerManifest in peerStore.GetAll())
        {
            if (peerManifest.StreamType != ManifestStreamType.Social) continue;
            lock (peerManifest)
            {
                allVotes.AddRange(peerManifest.Operations.Where(o =>
                    o.OperationType == ManifestOperationType.CompetitionCastVote &&
                    o.TargetId == compId));
            }
        }

        // From local
        lock (localSocialManifest)
        {
            allVotes.AddRange(localSocialManifest.Operations.Where(o =>
                o.OperationType == ManifestOperationType.CompetitionCastVote &&
                o.TargetId == compId));
        }

        // 2. Decrypt and count
        var trackScores = new Dictionary<string, int>();

        foreach (var voteOp in allVotes)
        {
            if (string.IsNullOrEmpty(voteOp.ContentHash)) continue;

            string? decrypted = null;
            try
            {
                decrypted = CryptoService.DecryptData(voteOp.ContentHash, privateKeyPem);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Exception decrypting vote {0} from user {1} for competition {2}", voteOp.OperationId, voteOp.TargetId, compId);
                continue;
            }

            if (string.IsNullOrEmpty(decrypted))
            {
                logger.Warn("Failed to decrypt vote {0} from user {1} for competition {2}", voteOp.OperationId, voteOp.TargetId, compId);
                continue;
            }

            try
            {
                // The payload might just be the TrackId, or maybe a JSON object. We'll assume the payload IS the TrackId for a simple vote.
                // Depending on how `CompetitionCastVote` sets `ContentHash`.
                // Let's assume the decrypted text is the target TrackId.
                var votedTrackId = decrypted.Trim();
                if (!string.IsNullOrEmpty(votedTrackId))
                {
                    if (trackScores.ContainsKey(votedTrackId)) trackScores[votedTrackId]++;
                    else trackScores[votedTrackId] = 1;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to parse decrypted vote {0}", voteOp.OperationId);
            }
        }

        // 3. Rank tracks
        var rankedTracks = trackScores
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        // 4. Publish Results
        var result = new CompetitionResult
        {
            OrderedTrackIds = rankedTracks,
            TallyTimestamp = DateTime.UtcNow,
            AdministratorSignature = "" // Will be set after serialization
        };

        var resultJson = JsonSerializer.Serialize(result);
        var signature = CryptoService.SignData(resultJson, privateKeyPem);
        result.AdministratorSignature = signature;

        var finalJson = JsonSerializer.Serialize(result);

        logger.Info("Publishing results for competition {0} with {1} tracks ranked.", compId, rankedTracks.Count);

        syncOrchestrator.RecordCompetitionRevealResults(compId, finalJson);

        return Task.CompletedTask;
    }
}
