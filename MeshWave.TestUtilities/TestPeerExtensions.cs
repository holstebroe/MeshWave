using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;

namespace MeshWave.TestUtilities;

public static class TestPeerExtensions
{
    public static void CommentOn(this TestPeer peer, string targetId, string text)
    {
        peer.Orchestrator.RecordComment(targetId, text);
    }

    public static void Like(this TestPeer peer, string targetId)
    {
        peer.Orchestrator.RecordLike(targetId);
    }

    public static void Play(this TestPeer peer, string targetId, string title = "Unknown", string artist = "Unknown")
    {
        peer.Orchestrator.RecordPlay(targetId, title, artist);
    }

    public static bool HasOperation(this TestPeer peer, string fromUserId, ManifestStreamType streamType, Func<ManifestOperation, bool> predicate)
    {
        var manifest = peer.GetPeerManifest(fromUserId, streamType);
        if (manifest == null && fromUserId == peer.UserId)
        {
            manifest = peer.GetLocalManifest(streamType);
        }

        return manifest?.Operations.Any(predicate) ?? false;
    }

    public static async Task WaitForConditionAsync(this TestPeer peer, Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(100);
            if (deadline.Subtract(DateTime.UtcNow).TotalMilliseconds % 500 < 100)
            {
                await peer.SyncAsync();
            }
        }
        throw new TimeoutException("Condition not met within timeout.");
    }
}
