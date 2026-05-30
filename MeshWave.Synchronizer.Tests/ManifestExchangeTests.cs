using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class ManifestExchangeTests : IAsyncDisposable
{
    private const int TestPort = 39980;
    private readonly ManifestExchangeServer _server = new(TestPort);
    private readonly ManifestExchangeClient _client = new(timeoutMs: 5000);
    private readonly ManifestManager _manager = new();

    [Fact]
    public async Task FetchManifest_ReturnsServerManifest()
    {
        var manifest = _manager.CreateManifest("user-1");

        await _server.StartAsync(() => manifest);

        try
        {
            var fetched = await _client.FetchManifestAsync("127.0.0.1", TestPort);

            Assert.NotNull(fetched);
            Assert.Equal("user-1", fetched.UserId);
        }
        finally
        {
            await _server.StopAsync();
        }
    }

    [Fact]
    public async Task PushManifest_ServerRaisesManifestReceivedEvent()
    {
        using var serverWithDifferentPort = new ManifestExchangeServer(TestPort + 1);
        var receivedManifest = new TaskCompletionSource<Manifest>(TaskCreationOptions.RunContinuationsAsynchronously);
        serverWithDifferentPort.ManifestReceived += (_, e) => receivedManifest.TrySetResult(e.Manifest);

        var emptyManifest = _manager.CreateManifest("user-serving");
        await serverWithDifferentPort.StartAsync(() => emptyManifest);

        try
        {
            var toSend = _manager.CreateManifest("user-sender");
            var client = new ManifestExchangeClient(timeoutMs: 5000);
            var ack = await client.PushManifestAsync("127.0.0.1", TestPort + 1, toSend);

            Assert.True(ack);

            var completed = await Task.WhenAny(receivedManifest.Task, Task.Delay(5000));
            Assert.True(receivedManifest.Task.IsCompleted);
            Assert.Equal("user-sender", receivedManifest.Task.Result.UserId);
        }
        finally
        {
            await serverWithDifferentPort.StopAsync();
        }
    }

    [Fact]
    public async Task FetchManifest_ReturnsNullManifestWhenServerHasNone()
    {
        using var serverWithDifferentPort = new ManifestExchangeServer(TestPort + 2);
        await serverWithDifferentPort.StartAsync(() => null);

        try
        {
            var client = new ManifestExchangeClient(timeoutMs: 5000);
            var fetched = await client.FetchManifestAsync("127.0.0.1", TestPort + 2);

            Assert.Null(fetched);
        }
        finally
        {
            await serverWithDifferentPort.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
    }
}
