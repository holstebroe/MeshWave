using System.Net;
using System.Net.Sockets;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class ManifestExchangeTests : IAsyncDisposable
{
    private readonly ManifestExchangeClient _client = new(timeoutMs: 10000);
    private readonly ManifestManager _manager = new();

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task FetchManifest_ReturnsServerManifest()
    {
        var port = FindFreePort();
        using var server = new ManifestExchangeServer(port);
        var manifest = _manager.CreateManifest("user-1");

        await server.StartAsync(_ => manifest, cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var fetched = await _client.FetchManifestAsync("127.0.0.1", port, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(fetched);
            Assert.Equal("user-1", fetched.UserId);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task PushManifest_ServerRaisesManifestReceivedEvent()
    {
        var port = FindFreePort();
        using var server = new ManifestExchangeServer(port);
        var receivedManifest = new TaskCompletionSource<Manifest>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ManifestReceived += (_, e) => receivedManifest.TrySetResult(e.Manifest);

        var emptyManifest = _manager.CreateManifest("user-serving");
        await server.StartAsync(_ => emptyManifest, cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var toSend = _manager.CreateManifest("user-sender");
            var client = new ManifestExchangeClient(timeoutMs: 10000);
            var ack = await client.PushManifestAsync("127.0.0.1", port, toSend, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(ack);

            var completed = await Task.WhenAny(receivedManifest.Task, Task.Delay(10000, TestContext.Current.CancellationToken));
            Assert.True(receivedManifest.Task.IsCompleted);
            var result = await receivedManifest.Task;
            Assert.Equal("user-sender", result.UserId);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task FetchManifest_ReturnsNullManifestWhenServerHasNone()
    {
        var port = FindFreePort();
        using var server = new ManifestExchangeServer(port);
        await server.StartAsync(_ => null, cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var client = new ManifestExchangeClient(timeoutMs: 10000);
            var fetched = await client.FetchManifestAsync("127.0.0.1", port, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(fetched);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RequestRendezvous_ReturnsSessionFromServerProvider()
    {
        var port = FindFreePort();
        using var server = new ManifestExchangeServer(port);
        await server.StartAsync(
            _ => null,
            peersProvider: null,
            rendezvousProvider: request => new RendezvousResponse
            {
                Success = true,
                SessionId = $"rv-{request.InitiatorUserId}-{request.TargetUserId}",
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30),
                Message = "ok"
            },
            cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var client = new ManifestExchangeClient(timeoutMs: 15000);
            var response = await client.RequestRendezvousAsync("127.0.0.1", port, new RendezvousRequest
            {
                InitiatorUserId = "initiator-1",
                TargetUserId = "target-1",
                InitiatorPort = 40001
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(response);
            Assert.True(response!.Success);
            Assert.Contains("initiator-1", response.SessionId, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task FetchManifest_WithRange_ReturnsFilteredOperations()
    {
        var port = FindFreePort();
        using var server = new ManifestExchangeServer(port);
        var manifest = _manager.CreateManifest("user-range");
        for (var i = 0; i < 5; i++)
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

        await server.StartAsync(_ => manifest, cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var client = new ManifestExchangeClient(timeoutMs: 10000);

            // Request middle range (2, 3)
            var fetched = await client.FetchManifestAsync("127.0.0.1", port, startSequenceNumber: 2, endSequenceNumber: 3, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(fetched);
            Assert.Equal(2, fetched.Operations.Count);
            Assert.Equal(2, fetched.Operations[0].SequenceNumber);
            Assert.Equal(3, fetched.Operations[1].SequenceNumber);

            // Request from 4 onwards
            var fetched2 = await client.FetchManifestAsync("127.0.0.1", port, startSequenceNumber: 4, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(fetched2);
            Assert.Single(fetched2.Operations);
            Assert.Equal(4, fetched2.Operations[0].SequenceNumber);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
