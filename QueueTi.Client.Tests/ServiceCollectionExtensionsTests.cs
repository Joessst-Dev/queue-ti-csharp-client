using Microsoft.Extensions.DependencyInjection;
using QueueTi.Client.Tests.Fixtures;
using QueueTi.Extensions;

namespace QueueTi.Client.Tests;

public sealed class ServiceCollectionExtensionsTests : IAsyncLifetime
{
    private GrpcTestServer _server = null!;

    // exp=9999999999
    private const string ValidJwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.sig";

    public async Task InitializeAsync() =>
        _server = await GrpcTestServer.CreateAsync();

    public async Task DisposeAsync() =>
        await _server.DisposeAsync();

    [Fact]
    public async Task AddQueueTiClient_GivenValidConfig_ShouldResolveSingletonClient()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddQueueTiClient("http://localhost", o =>
        {
            o.Insecure = true;
            o.ConfigureHttpClientBuilder = b =>
                b.ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler());
        });

        // Act (When)
        await using var provider = services.BuildServiceProvider();
        var client1 = provider.GetRequiredService<QueueTiClient>();
        var client2 = provider.GetRequiredService<QueueTiClient>();

        // Assert (Then)
        Assert.NotNull(client1);
        Assert.Same(client1, client2);
    }

    [Fact]
    public async Task AddQueueTiClient_GivenBearerToken_ShouldSendTokenOnRpc()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddQueueTiClient("http://localhost", o =>
        {
            o.BearerToken = ValidJwt;
            o.ConfigureHttpClientBuilder = b =>
                b.ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler());
        });

        // Act (When)
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<QueueTiClient>();
        await client.NewProducer().PublishAsync("di-bearer-token", "p"u8.ToArray());

        // Assert (Then)
        var headers = _server.Fake.LastRequestHeaders;
        Assert.NotNull(headers);
        Assert.Contains(headers, e => e.Key == "authorization");
    }

    [Fact]
    public async Task AddQueueTiClient_GivenBearerTokenAndSetToken_ShouldUseUpdatedToken()
    {
        // Arrange (Given)
        const string newToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.updated";
        var services = new ServiceCollection();
        services.AddQueueTiClient("http://localhost", o =>
        {
            o.BearerToken = ValidJwt;
            o.ConfigureHttpClientBuilder = b =>
                b.ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler());
        });

        // Act (When)
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<QueueTiClient>();
        client.SetToken(newToken);
        await client.NewProducer().PublishAsync("di-set-token", "p"u8.ToArray());

        // Assert (Then)
        var headers = _server.Fake.LastRequestHeaders;
        Assert.NotNull(headers);
        var authEntry = headers.FirstOrDefault(e => e.Key == "authorization");
        Assert.NotNull(authEntry);
        Assert.Equal("Bearer " + newToken, authEntry.Value);
    }
}
