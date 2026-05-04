using Grpc.Core;
using QueueTi.Client.Tests.Fixtures;
using QueueTi.Pb;

namespace QueueTi.Client.Tests;

public sealed class ConsumerErrorTests : IAsyncLifetime
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
    public async Task ConsumeAsync_GivenStreamError_ShouldReconnectAndReceiveMessage()
    {
        // Arrange (Given)
        var topic = "consumer-error-reconnect";
        _server.Fake.FailNextSubscribeWith(StatusCode.Unavailable, "server gone");

        var received = new TaskCompletionSource<QueueTiMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "g-reconnect" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act (When)
        var consumeTask = consumer.ConsumeAsync((msg, ct) =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        }, cts.Token);

        await Task.Delay(50, cts.Token);
        await _server.Fake.EnqueueForTestAsync(topic, "hello"u8.ToArray());

        var message = await received.Task;
        await _server.Fake.WaitForAckAsync(cts.Token);
        await cts.CancelAsync();
        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        Assert.Equal(topic, message.Topic);
    }

    [Fact]
    public async Task ConsumeAsync_GivenNackFails_ShouldLogAndContinue()
    {
        // Arrange (Given)
        var topic = "consumer-error-nack-fails";
        _server.Fake.FailNextNack(new RpcException(new Status(StatusCode.Internal, "nack failed")));

        var handlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "g-nack-fail" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await _server.Fake.EnqueueForTestAsync(topic, "payload"u8.ToArray());

        // Act (When)
        var consumeTask = consumer.ConsumeAsync((msg, ct) =>
        {
            handlerCalled.TrySetResult();
            throw new InvalidOperationException("force nack");
        }, cts.Token);

        await handlerCalled.Task;
        await Task.Delay(300);
        await cts.CancelAsync();
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await consumeTask; } catch (OperationCanceledException) { }
        });

        // Assert (Then)
        Assert.Null(ex);
    }

    [Fact]
    public async Task ConsumeAsync_GivenVisibilityTimeout_ShouldPassItToSubscribeRequest()
    {
        // Arrange (Given)
        var topic = "consumer-error-visibility-subscribe";
        var consumer = _client.NewConsumer(topic,
            new ConsumerOptions { ConsumerGroup = "g-vis", VisibilityTimeoutSeconds = 42 });

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
        Assert.NotNull(_server.Fake.LastSubscribeRequest);
        Assert.Equal(42u, _server.Fake.LastSubscribeRequest!.VisibilityTimeoutSeconds);
    }

    [Fact]
    public async Task ConsumeBatchAsync_GivenVisibilityTimeout_ShouldPassItToBatchDequeueRequest()
    {
        // Arrange (Given)
        var topic = "consumer-error-visibility-batch";
        var consumer = _client.NewConsumer(topic,
            new ConsumerOptions { ConsumerGroup = "g-batch-vis", VisibilityTimeoutSeconds = 99 });

        var producer = _client.NewProducer();
        await producer.PublishAsync(topic, "msg1"u8.ToArray());
        await producer.PublishAsync(topic, "msg2"u8.ToArray());

        var batchReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act (When)
        var consumeTask = consumer.ConsumeBatchAsync(5, (messages, ct) =>
        {
            batchReceived.TrySetResult();
            return Task.CompletedTask;
        }, cts.Token);

        await batchReceived.Task;
        await cts.CancelAsync();
        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        Assert.NotNull(_server.Fake.LastBatchDequeueRequest);
        Assert.Equal(99u, _server.Fake.LastBatchDequeueRequest!.VisibilityTimeoutSeconds);
    }

    [Fact]
    public async Task ConsumeBatchAsync_GivenInitiallyEmptyQueue_ShouldReceiveMessageAddedLater()
    {
        // Arrange (Given)
        var topic = "consumer-error-batch-empty-then-fill";
        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = "g-late-batch" });

        IReadOnlyList<QueueTiMessage>? receivedBatch = null;
        var batchReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var producer = _client.NewProducer();

        // Act (When)
        var consumeTask = consumer.ConsumeBatchAsync(5, (messages, ct) =>
        {
            receivedBatch = messages;
            batchReceived.TrySetResult();
            return Task.CompletedTask;
        }, cts.Token);

        await Task.Delay(200);
        await producer.PublishAsync(topic, "late-msg"u8.ToArray());

        await batchReceived.Task;
        await cts.CancelAsync();
        try { await consumeTask; } catch (OperationCanceledException) { }

        // Assert (Then)
        Assert.NotNull(receivedBatch);
        Assert.NotEmpty(receivedBatch);
    }
}
