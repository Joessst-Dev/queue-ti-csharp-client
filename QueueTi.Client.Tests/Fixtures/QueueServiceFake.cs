using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using QueueTi.Pb;

namespace QueueTi.Client.Tests.Fixtures;

public sealed class QueueServiceFake : QueueService.QueueServiceBase
{
    private readonly ConcurrentDictionary<string, Channel<SubscribeResponse>> _subscribeChannels = new();
    private readonly ConcurrentBag<string> _ackedIds = [];
    private readonly ConcurrentBag<(string id, string reason)> _nackedMessages = [];
    private readonly ConcurrentQueue<DequeueResponse> _dequeueQueue = new();
    private readonly SemaphoreSlim _ackSignal = new(0);

    public IReadOnlyList<string> AckedIds => _ackedIds.ToList();
    public IReadOnlyList<(string id, string reason)> NackedMessages => _nackedMessages.ToList();

    public Task WaitForAckAsync(CancellationToken ct = default) =>
        _ackSignal.WaitAsync(ct);

    public override Task<EnqueueResponse> Enqueue(EnqueueRequest request, ServerCallContext context)
    {
        var id = Guid.NewGuid().ToString();
        var stored = new DequeueResponse
        {
            Id = id,
            Topic = request.Topic,
            Payload = request.Payload,
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            RetryCount = 0,
            MaxRetries = 3
        };
        foreach (var (k, v) in request.Metadata)
            stored.Metadata[k] = v;
        if (request.HasKey)
            stored.Key = request.Key;

        _dequeueQueue.Enqueue(stored);

        return Task.FromResult(new EnqueueResponse { Id = id });
    }

    public override Task<DequeueResponse> Dequeue(DequeueRequest request, ServerCallContext context)
    {
        var skipped = new List<DequeueResponse>();
        DequeueResponse? found = null;

        while (_dequeueQueue.TryDequeue(out var msg))
        {
            if (msg.Topic == request.Topic && found is null) found = msg;
            else skipped.Add(msg);
            if (found is not null) break;
        }
        foreach (var msg in skipped) _dequeueQueue.Enqueue(msg);
        return Task.FromResult(found ?? new DequeueResponse());
    }

    public override Task<BatchDequeueResponse> BatchDequeue(BatchDequeueRequest request, ServerCallContext context)
    {
        var response = new BatchDequeueResponse();
        var skipped = new List<DequeueResponse>();
        var taken = 0;

        while (taken < request.Count && _dequeueQueue.TryDequeue(out var msg))
        {
            if (msg.Topic == request.Topic) { response.Messages.Add(msg); taken++; }
            else skipped.Add(msg);
        }
        foreach (var msg in skipped) _dequeueQueue.Enqueue(msg);
        return Task.FromResult(response);
    }

    public override Task<AckResponse> Ack(AckRequest request, ServerCallContext context)
    {
        _ackedIds.Add(request.Id);
        _ackSignal.Release();
        return Task.FromResult(new AckResponse());
    }

    public override Task<NackResponse> Nack(NackRequest request, ServerCallContext context)
    {
        _nackedMessages.Add((request.Id, request.Error));
        return Task.FromResult(new NackResponse());
    }

    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<SubscribeResponse> responseStream,
        ServerCallContext context)
    {
        var channel = _subscribeChannels.GetOrAdd(request.Topic,
            _ => Channel.CreateUnbounded<SubscribeResponse>());

        await foreach (var msg in channel.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(msg);
        }
    }

    public async Task EnqueueForTestAsync(string topic, byte[] payload, Dictionary<string, string>? metadata = null)
    {
        var channel = _subscribeChannels.GetOrAdd(topic,
            _ => Channel.CreateUnbounded<SubscribeResponse>());

        var response = new SubscribeResponse
        {
            Id = Guid.NewGuid().ToString(),
            Topic = topic,
            Payload = ByteString.CopyFrom(payload),
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            RetryCount = 0
        };

        if (metadata is not null)
            foreach (var (k, v) in metadata)
                response.Metadata[k] = v;

        await channel.Writer.WriteAsync(response);
    }
}
