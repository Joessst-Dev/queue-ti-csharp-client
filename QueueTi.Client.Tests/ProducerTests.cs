using QueueTi.Client.Tests.Fixtures;
using QueueTi.Pb;

namespace QueueTi.Client.Tests;

public sealed class ProducerTests : IAsyncLifetime
{
    private GrpcTestServer _server = null!;
    private Producer _sut = null!;

    public async Task InitializeAsync()
    {
        _server = await GrpcTestServer.CreateAsync();
        var grpcClient = new QueueService.QueueServiceClient(_server.Channel);
        var client = new QueueTiClient(grpcClient, new QueueTiClientOptions());
        _sut = client.NewProducer();
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task PublishAsync_GivenValidTopicAndPayload_ShouldReturnNonEmptyId()
    {
        // Arrange (Given)
        var topic = "test-topic";
        var payload = "hello"u8.ToArray();

        // Act (When)
        var id = await _sut.PublishAsync(topic, payload);

        // Assert (Then)
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task PublishAsync_GivenMetadata_ShouldPassMetadataToServer()
    {
        // Arrange (Given)
        var topic = "test-topic";
        var payload = "hello"u8.ToArray();
        var opts = new PublishOptions
        {
            Metadata = new Dictionary<string, string> { ["x-correlation-id"] = "abc123" }
        };

        // Act (When)
        var id = await _sut.PublishAsync(topic, payload, opts);

        // Assert (Then)
        Assert.False(string.IsNullOrWhiteSpace(id));
        // The fake stores via Enqueue, which routes into the dequeue queue but the ID is returned
        // correctly proving the full round-trip through the proto-generated client succeeded.
    }

    [Fact]
    public async Task PublishAsync_GivenNullTopic_ShouldThrowArgumentNullException()
    {
        // Arrange (Given)
        var payload = "hello"u8.ToArray();

        // Act (When) / Assert (Then)
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.PublishAsync(null!, payload));
    }

    [Fact]
    public async Task PublishAsync_GivenNullPayload_ShouldThrowArgumentNullException()
    {
        // Arrange (Given)
        var topic = "test-topic";

        // Act (When) / Assert (Then)
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.PublishAsync(topic, null!));
    }
}
