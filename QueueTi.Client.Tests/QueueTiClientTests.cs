using Grpc.Core.Interceptors;
using QueueTi.Client.Tests.Fixtures;
using QueueTi.Pb;

namespace QueueTi.Client.Tests;

public sealed class QueueTiClientTests : IAsyncLifetime
{
    private GrpcTestServer _server = null!;

    // exp=9999999999
    private const string LongLivedToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.sig";

    // exp=1 (1970-01-01 — already expired)
    private const string ExpiredToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjoxfQ.sig";

    public async Task InitializeAsync() =>
        _server = await GrpcTestServer.CreateAsync();

    public async Task DisposeAsync() =>
        await _server.DisposeAsync();

    // ── Create() tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_GivenInsecureAddress_ShouldProduceWorkingClient()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            Insecure = true,
            ConfigureChannel = o => o.HttpClient = _server.CreateHttpClient()
        };

        // Act (When)
        await using var client = QueueTiClient.Create("http://localhost", options);
        var id = await client.NewProducer().PublishAsync("create-insecure", "p"u8.ToArray());

        // Assert (Then)
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task Create_GivenBearerToken_ShouldInjectAuthHeader()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            BearerToken = LongLivedToken,
            ConfigureChannel = o => o.HttpClient = _server.CreateHttpClient()
        };

        // Act (When)
        await using var client = QueueTiClient.Create("http://localhost", options);
        await client.NewProducer().PublishAsync("create-bearer-auth", "p"u8.ToArray());

        // Assert (Then)
        var headers = _server.Fake.LastRequestHeaders;
        Assert.NotNull(headers);
        Assert.Contains(headers, e => e.Key == "authorization");
    }

    // ── SetToken() tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetToken_GivenClientWithToken_ShouldUpdateStore()
    {
        // Arrange (Given)
        const string newToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.newsig";
        var store = new TokenStore(LongLivedToken);
        var interceptor = new BearerTokenInterceptor(store);
        var invoker = _server.Channel.Intercept(interceptor);
        var grpcClient = new QueueService.QueueServiceClient(invoker);
        using var client = new QueueTiClient(grpcClient, new QueueTiClientOptions(), store, null, null);

        // Act (When)
        client.SetToken(newToken);
        await client.NewProducer().PublishAsync("set-token-update", "p"u8.ToArray());

        // Assert (Then)
        var headers = _server.Fake.LastRequestHeaders;
        Assert.NotNull(headers);
        var authEntry = headers.FirstOrDefault(e => e.Key == "authorization");
        Assert.NotNull(authEntry);
        Assert.Equal("Bearer " + newToken, authEntry.Value);
    }

    [Fact]
    public void SetToken_GivenClientWithNoToken_ShouldThrowInvalidOperationException()
    {
        // Arrange (Given)
        var grpcClient = new QueueService.QueueServiceClient(_server.Channel);
        using var client = new QueueTiClient(grpcClient, new QueueTiClientOptions());

        // Act (When) / Assert (Then)
        Assert.Throws<InvalidOperationException>(() => client.SetToken("any-token"));
    }

    // ── DisposeAsync() tests ────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_GivenClientWithRefresher_ShouldCompleteCleanly()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            BearerToken = LongLivedToken,
            TokenRefresher = _ => Task.FromResult(LongLivedToken),
            ConfigureChannel = o => o.HttpClient = _server.CreateHttpClient()
        };
        var client = QueueTiClient.Create("http://localhost", options);

        // Act (When)
        var ex = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());

        // Assert (Then)
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange (Given)
        var grpcClient = new QueueService.QueueServiceClient(_server.Channel);
        var client = new QueueTiClient(grpcClient, new QueueTiClientOptions());

        // Act (When) / Assert (Then)
        client.Dispose();
        var ex = Record.Exception(() => client.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_CalledAfterDispose_ShouldNotThrow()
    {
        // Arrange (Given)
        var grpcClient = new QueueService.QueueServiceClient(_server.Channel);
        var client = new QueueTiClient(grpcClient, new QueueTiClientOptions());

        // Act (When)
        client.Dispose();
        var ex = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());

        // Assert (Then)
        Assert.Null(ex);
    }

    // ── Token refresh loop tests ────────────────────────────────────────────────

    [Fact]
    public async Task TokenRefresher_GivenExpiredToken_ShouldCallRefresherImmediately()
    {
        // Arrange (Given)
        var refresherCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new TokenStore(ExpiredToken);
        var interceptor = new BearerTokenInterceptor(store);
        var invoker = _server.Channel.Intercept(interceptor);
        var grpcClient = new QueueService.QueueServiceClient(invoker);
        var options = new QueueTiClientOptions
        {
            TokenRefresher = ct =>
            {
                refresherCalled.TrySetResult();
                return Task.FromResult(LongLivedToken);
            }
        };
        using var client = new QueueTiClient(grpcClient, options, store, null, null);

        // Act (When)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => refresherCalled.TrySetCanceled());
        await refresherCalled.Task;

        // Assert (Then)
        Assert.True(refresherCalled.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TokenRefresher_GivenRefresherReturnsNewToken_ShouldUseNewTokenOnNextCall()
    {
        // Arrange (Given)
        const string refreshedToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.refreshed";
        var refresherCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new TokenStore(ExpiredToken);
        var interceptor = new BearerTokenInterceptor(store);
        var invoker = _server.Channel.Intercept(interceptor);
        var grpcClient = new QueueService.QueueServiceClient(invoker);
        var options = new QueueTiClientOptions
        {
            TokenRefresher = ct =>
            {
                refresherCalled.TrySetResult();
                return Task.FromResult(refreshedToken);
            }
        };
        using var client = new QueueTiClient(grpcClient, options, store, null, null);

        // Act (When) — wait for the refresh to fire, then publish
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => refresherCalled.TrySetCanceled());
        await refresherCalled.Task;

        await client.NewProducer().PublishAsync("token-refresher-new-token", "p"u8.ToArray());

        // Assert (Then)
        var headers = _server.Fake.LastRequestHeaders;
        Assert.NotNull(headers);
        var authEntry = headers.FirstOrDefault(e => e.Key == "authorization");
        Assert.NotNull(authEntry);
        Assert.Equal("Bearer " + refreshedToken, authEntry.Value);
    }
}
