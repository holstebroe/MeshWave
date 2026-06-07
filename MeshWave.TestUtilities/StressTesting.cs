using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Models;

namespace MeshWave.TestUtilities;

public static class StressTesting
{
    public static void FloodWithComments(TestPeer peer, string targetId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            peer.CommentOn(targetId, $"Stress comment {i} from {peer.Name}");
        }
    }

    public static void FloodWithPlays(TestPeer peer, string targetId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            peer.Play(targetId, $"Stress Title {i}", "Stress Artist");
        }
    }
}

public static class TestTraits
{
    public const string Category = "Category";
    public const string Performance = "Performance";
    public const string Stress = "Stress";
}
