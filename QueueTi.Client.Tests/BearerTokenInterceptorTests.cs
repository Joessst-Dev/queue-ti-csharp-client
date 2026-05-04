using Grpc.Core.Interceptors;
using QueueTi.Client.Tests.Fixtures;
using QueueTi.Pb;

namespace QueueTi.Client.Tests;

public sealed class BearerTokenInterceptorTests : IAsyncLifetime
{
    private GrpcTestServer _server = null!;
    private QueueTiClient _client = null!;
    private TokenStore _store = null!;

    // Long-lived token: exp = 9999999999
    private const string LongLivedToken =
        "header.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.sig";

    public async Task InitializeAsync()
    {
        _server = await GrpcTestServer.CreateAsync();
        _store = new TokenStore(LongLivedToken);
        var interceptor = new BearerTokenInterceptor(_store);
        var invoker = _server.Channel.Intercept(interceptor);
        var grpcClient = new QueueService.QueueServiceClient(invoker);
        _client = new QueueTiClient(grpcClient, new QueueTiClientOptions(), _store, null, null);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _store.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task UnaryCall_GivenBearerToken_ShouldIncludeAuthorizationHeader()
    {
        // Arrange (Given)
        var topic = "interceptor-unary-auth";
        var producer = _client.NewProducer();

        // Act (When)
        await producer.PublishAsync(topic, "payload"u8.ToArray());

        // Assert (Then)
        var headers = _server.Fake.LastRequestHeaders;
        Assert.NotNull(headers);
        var authEntry = headers.FirstOrDefault(e => e.Key == "authorization");
        Assert.NotNull(authEntry);
        Assert.Equal("Bearer " + LongLivedToken, authEntry.Value);
    }

    [Fact]
    public async Task ServerStreamingCall_GivenBearerToken_ShouldIncludeAuthorizationHeader()
    {
        // Arrange (Given)
        var topic = "interceptor-streaming-auth";
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "g1" });
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act (When)
        var consumeTask = consumer.ConsumeAsync((msg, ct) =>
        {
            received.TrySetResult();
            return Task.CompletedTask;
        }, cts.Token);

        await _server.Fake.EnqueueForTestAsync(topic, "p"u8.ToArray());
        await received.Task;
        await cts.CancelAsync();
        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        var headers = _server.Fake.LastSubscribeHeaders;
        Assert.NotNull(headers);
        var authEntry = headers.FirstOrDefault(e => e.Key == "authorization");
        Assert.NotNull(authEntry);
        Assert.Equal("Bearer " + LongLivedToken, authEntry.Value);
    }

    [Fact]
    public async Task UnaryCall_GivenExistingHeaders_ShouldNotDuplicateAuthorizationHeader()
    {
        // Arrange (Given)
        var topic = "interceptor-no-dup-auth";
        var producer = _client.NewProducer();

        // Act (When)
        await producer.PublishAsync(topic, "p1"u8.ToArray());
        await producer.PublishAsync(topic, "p2"u8.ToArray());

        // Assert (Then)
        var allHeaders = _server.Fake.RequestHeadersHistory.ToList();
        Assert.Equal(2, allHeaders.Count);
        foreach (var headers in allHeaders)
        {
            var authEntries = headers.Where(e => e.Key == "authorization").ToList();
            Assert.Single(authEntries);
        }
    }

    [Fact]
    public async Task SetToken_GivenNewToken_ShouldUseUpdatedTokenOnNextCall()
    {
        // Arrange (Given)
        var topic = "interceptor-set-token";
        var producer = _client.NewProducer();
        const string newToken = "header.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.newsig";

        await producer.PublishAsync(topic, "before"u8.ToArray());

        // Act (When)
        _client.SetToken(newToken);
        await producer.PublishAsync(topic, "after"u8.ToArray());

        // Assert (Then)
        var history = _server.Fake.RequestHeadersHistory.ToList();
        Assert.Equal(2, history.Count);
        var secondCallAuth = history[1].FirstOrDefault(e => e.Key == "authorization");
        Assert.NotNull(secondCallAuth);
        Assert.Equal("Bearer " + newToken, secondCallAuth.Value);
    }
}
