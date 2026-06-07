using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class GroundednessTests
{
    [Fact]
    public async Task FetchPeersAsync_ReturnsNull_OnConnectionFailure()
    {
        // Use a port that is unlikely to be listening
        var client = new ManifestExchangeClient(timeoutMs: 500);
        var result = await client.FetchPeersAsync("127.0.0.1", 1, cancellationToken: TestContext.Current.CancellationToken); // Port 1 usually refused or timed out

        Assert.Null(result);
    }
}
