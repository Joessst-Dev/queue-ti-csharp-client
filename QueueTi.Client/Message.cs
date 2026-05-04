using Google.Protobuf.WellKnownTypes;
using QueueTi.Pb;

namespace QueueTi;

public sealed class QueueTiMessage
{
    private readonly Func<CancellationToken, Task> _ack;
    private readonly Func<string, CancellationToken, Task> _nack;

    private QueueTiMessage(
        string id,
        string topic,
        byte[] payload,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset createdAt,
        int retryCount,
        Func<CancellationToken, Task> ack,
        Func<string, CancellationToken, Task> nack)
    {
        Id = id;
        Topic = topic;
        Payload = payload;
        Metadata = metadata;
        CreatedAt = createdAt;
        RetryCount = retryCount;
        _ack = ack;
        _nack = nack;
    }

    public string Id { get; }
    public string Topic { get; }
    public byte[] Payload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public DateTimeOffset CreatedAt { get; }
    public int RetryCount { get; }

    public Task AckAsync(CancellationToken ct = default) => _ack(ct);
    public Task NackAsync(string reason, CancellationToken ct = default) => _nack(reason, ct);

    internal static QueueTiMessage FromSubscribeResponse(
        SubscribeResponse response,
        string consumerGroup,
        QueueService.QueueServiceClient grpcClient)
    {
        return new QueueTiMessage(
            id: response.Id,
            topic: response.Topic,
            payload: response.Payload.ToByteArray(),
            metadata: response.Metadata,
            createdAt: response.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
            retryCount: response.RetryCount,
            ack: ct => grpcClient.AckAsync(
                new AckRequest { Id = response.Id, ConsumerGroup = consumerGroup },
                cancellationToken: ct).ResponseAsync,
            nack: (reason, ct) => grpcClient.NackAsync(
                new NackRequest { Id = response.Id, Error = reason, ConsumerGroup = consumerGroup },
                cancellationToken: ct).ResponseAsync);
    }

    internal static QueueTiMessage FromDequeueResponse(
        DequeueResponse response,
        string consumerGroup,
        QueueService.QueueServiceClient grpcClient)
    {
        return new QueueTiMessage(
            id: response.Id,
            topic: response.Topic,
            payload: response.Payload.ToByteArray(),
            metadata: response.Metadata,
            createdAt: response.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
            retryCount: response.RetryCount,
            ack: ct => grpcClient.AckAsync(
                new AckRequest { Id = response.Id, ConsumerGroup = consumerGroup },
                cancellationToken: ct).ResponseAsync,
            nack: (reason, ct) => grpcClient.NackAsync(
                new NackRequest { Id = response.Id, Error = reason, ConsumerGroup = consumerGroup },
                cancellationToken: ct).ResponseAsync);
    }
}
