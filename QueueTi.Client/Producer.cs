using Google.Protobuf;
using QueueTi.Pb;

namespace QueueTi;

public sealed class Producer
{
    private readonly QueueService.QueueServiceClient _grpcClient;

    internal Producer(QueueService.QueueServiceClient grpcClient)
    {
        _grpcClient = grpcClient;
    }

    public async Task<string> PublishAsync(
        string topic,
        byte[] payload,
        PublishOptions? opts = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(payload);

        var request = new EnqueueRequest
        {
            Topic = topic,
            Payload = ByteString.CopyFrom(payload)
        };

        if (opts?.Metadata is { } metadata)
            foreach (var (k, v) in metadata)
                request.Metadata[k] = v;

        if (opts?.Key is { } key)
            request.Key = key;

        var response = await _grpcClient.EnqueueAsync(request, cancellationToken: ct);
        return response.Id;
    }
}
