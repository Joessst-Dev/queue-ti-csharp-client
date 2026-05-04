using QueueTi.Client.Tests.Fixtures;
using QueueTi.Pb;

namespace QueueTi.Client.Tests;

public sealed class ConsumerTests : IAsyncLifetime
{
    private GrpcTestServer _server = null!;
    private QueueTiClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = await GrpcTestServer.CreateAsync();
        var grpcClient = new QueueService.QueueServiceClient(_server.Channel);
        _client = new QueueTiClient(grpcClient, new QueueTiClientOptions());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ConsumeAsync_GivenMessageOnTopic_ShouldReceiveMessageAndAck()
    {
        // Arrange (Given)
        var topic = "consume-ack-topic";
        var payload = "test-payload"u8.ToArray();
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "group1" });

        var received = new TaskCompletionSource<QueueTiMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await _server.Fake.EnqueueForTestAsync(topic, payload);

        // Act (When)
        var consumeTask = consumer.ConsumeAsync(async (msg, ct) =>
        {
            received.TrySetResult(msg);
            await Task.CompletedTask;
        }, cts.Token);

        var message = await received.Task;
        await Task.Delay(100); // let Ack complete
        await cts.CancelAsync();

        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        Assert.Equal(topic, message.Topic);
        Assert.Equal(payload, message.Payload);
        Assert.Contains(message.Id, _server.Fake.AckedIds);
    }

    [Fact]
    public async Task ConsumeAsync_GivenHandlerThatThrows_ShouldNackMessage()
    {
        // Arrange (Given)
        var topic = "consume-nack-topic";
        var payload = "fail-payload"u8.ToArray();
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "group-nack" });

        var handlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await _server.Fake.EnqueueForTestAsync(topic, payload);

        // Act (When)
        var consumeTask = consumer.ConsumeAsync(async (msg, ct) =>
        {
            handlerCalled.TrySetResult();
            await Task.CompletedTask;
            throw new InvalidOperationException("Simulated handler failure");
        }, cts.Token);

        await handlerCalled.Task;
        await Task.Delay(200); // let Nack complete
        await cts.CancelAsync();

        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        Assert.NotEmpty(_server.Fake.NackedMessages);
        Assert.Contains(_server.Fake.NackedMessages,
            n => n.reason == "Simulated handler failure");
    }

    [Fact]
    public async Task ConsumeBatchAsync_GivenMessagesInQueue_ShouldReceiveBatchWithAckCapability()
    {
        // Arrange (Given)
        var topic = "batch-topic";
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "batch-group" });

        var producer = _client.NewProducer();
        await producer.PublishAsync(topic, "msg1"u8.ToArray());
        await producer.PublishAsync(topic, "msg2"u8.ToArray());

        IReadOnlyList<QueueTiMessage>? receivedBatch = null;
        var batchReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act (When)
        var consumeTask = consumer.ConsumeBatchAsync(5, async (messages, ct) =>
        {
            receivedBatch = messages;
            batchReceived.TrySetResult();
            await Task.CompletedTask;
        }, cts.Token);

        await batchReceived.Task;
        await cts.CancelAsync();

        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        Assert.NotNull(receivedBatch);
        Assert.NotEmpty(receivedBatch);
        var first = receivedBatch[0];
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        // Verify AckAsync is callable (does not throw)
        await first.AckAsync();
    }
}
