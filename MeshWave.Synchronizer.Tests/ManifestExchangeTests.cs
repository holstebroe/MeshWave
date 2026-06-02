using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class ManifestExchangeTests : IAsyncDisposable
{
    private const int TestPort = 44100;
    private readonly ManifestExchangeServer _server = new(TestPort);
    private readonly ManifestExchangeClient _client = new(timeoutMs: 10000);
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
            var client = new ManifestExchangeClient(timeoutMs: 10000);
            var ack = await client.PushManifestAsync("127.0.0.1", TestPort + 1, toSend);

            Assert.True(ack);

            var completed = await Task.WhenAny(receivedManifest.Task, Task.Delay(10000));
            Assert.True(receivedManifest.Task.IsCompleted);
            var result = await receivedManifest.Task;
            Assert.Equal("user-sender", result.UserId);
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
            var client = new ManifestExchangeClient(timeoutMs: 10000);
            var fetched = await client.FetchManifestAsync("127.0.0.1", TestPort + 2);

            Assert.Null(fetched);
        }
        finally
        {
            await serverWithDifferentPort.StopAsync();
        }
    }

    [Fact]
    public async Task RequestRendezvous_ReturnsSessionFromServerProvider()
    {
        using var serverWithDifferentPort = new ManifestExchangeServer(TestPort + 3);
        await serverWithDifferentPort.StartAsync(
            () => null,
            peersProvider: null,
            rendezvousProvider: request => new RendezvousResponse
            {
                Success = true,
                SessionId = $"rv-{request.InitiatorUserId}-{request.TargetUserId}",
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30),
                Message = "ok"
            });

        try
        {
            var client = new ManifestExchangeClient(timeoutMs: 10000);
            var response = await client.RequestRendezvousAsync("127.0.0.1", TestPort + 3, new RendezvousRequest
            {
                InitiatorUserId = "initiator-1",
                TargetUserId = "target-1",
                InitiatorPort = 40001
            });

            Assert.NotNull(response);
            Assert.True(response!.Success);
            Assert.Contains("initiator-1", response.SessionId, StringComparison.Ordinal);
        }
        finally
        {
            await serverWithDifferentPort.StopAsync();
        }
    }

    [Fact]
    public async Task FetchManifest_WithRange_ReturnsFilteredOperations()
    {
        using var serverWithDifferentPort = new ManifestExchangeServer(TestPort + 4);
        var manifest = _manager.CreateManifest("user-range");
        for (int i = 0; i < 5; i++)
        {
            manifest.Operations.Add(new ManifestOperation
            {
                OperationId = $"op-{i}",
                OperationType = ManifestOperationType.Create,
                TargetId = $"track-{i}",
                TargetType = "Track",
                SequenceNumber = i,
                Signature = "sig",
                Timestamp = DateTime.UtcNow
            });
        }

        await serverWithDifferentPort.StartAsync(() => manifest);

        try
        {
            var client = new ManifestExchangeClient(timeoutMs: 10000);

            // Request middle range (2, 3)
            var fetched = await client.FetchManifestAsync("127.0.0.1", TestPort + 4, startSequenceNumber: 2, endSequenceNumber: 3);

            Assert.NotNull(fetched);
            Assert.Equal(2, fetched.Operations.Count);
            Assert.Equal(2, fetched.Operations[0].SequenceNumber);
            Assert.Equal(3, fetched.Operations[1].SequenceNumber);

            // Request from 4 onwards
            var fetched2 = await client.FetchManifestAsync("127.0.0.1", TestPort + 4, startSequenceNumber: 4);
            Assert.NotNull(fetched2);
            Assert.Single(fetched2.Operations);
            Assert.Equal(4, fetched2.Operations[0].SequenceNumber);
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
