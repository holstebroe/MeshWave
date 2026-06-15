using System;
using System.IO;
using System.Threading.Tasks;
using MeshWave.TestUtilities;
using MeshWave.Wpf.ViewModels;
using MeshWave.Synchronizer;

namespace MeshWave.Wpf.Services;

public static class MockScenarioRunner
{
    public static async Task ApplyScenarioAsync(string scenarioName, ApplicationViewModel applicationViewModel)
    {
        if (string.Equals(scenarioName, "EmptyLibrary", StringComparison.OrdinalIgnoreCase))
        {
            // Empty state, no peers
            return;
        }

        if (string.Equals(scenarioName, "RichCommunity", StringComparison.OrdinalIgnoreCase))
        {
            var manifestManager = new ManifestManager();
            int injectedPeers = 0;

            // Add dummy peers with tracks using TestPeerFactory
            for (int i = 1; i <= 5; i++)
            {
                var peer = TestPeerFactory.CreatePeer($"Peer{i}");

                try
                {
                    TestPeerFactory.InitializeWithTestData(peer, "Alice");
                }
                catch
                {
                    // If TestData/Alice doesn't exist, we just have empty peers which is fine
                }

                await peer.StartAsync();

                // Manually inject peer manifests into our local orchestrator's IManifestStore
                foreach(var streamType in Enum.GetValues<MeshWave.Common.Core.Models.ManifestStreamType>())
                {
                     var peerManifest = peer.GetLocalManifest(streamType);
                     if (peerManifest != null)
                     {
                         // Use MergeAndSave via the SyncOrchestrator's internal store logic, or we trigger it via reflection
                         // if we want to raise ManifestMerged
                         applicationViewModel.ManifestStore.MergeAndSave(peerManifest, peer.Identity.PublicKeyPem, manifestManager);
                     }
                }
                injectedPeers++;
            }

            // To ensure UI updates, we must inform the SyncOrchestrator that it's "connected" to peers and manifestations merged.
            // But we bypassed StartAsync.
            applicationViewModel.MockPeerCount(injectedPeers);
            return;
        }
    }
}
