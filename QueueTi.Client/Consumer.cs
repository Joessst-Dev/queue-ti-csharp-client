using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueueTi.Pb;

namespace QueueTi;

public sealed class Consumer
{
    private static readonly TimeSpan _minBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan _maxBackoff = TimeSpan.FromSeconds(30);

    private readonly QueueService.QueueServiceClient _grpcClient;
    private readonly string _topic;
    private readonly ConsumerOptions _options;
    private readonly ILogger<Consumer> _logger;

    internal Consumer(
        QueueService.QueueServiceClient grpcClient,
        string topic,
        ConsumerOptions options,
        ILogger<Consumer>? logger = null)
    {
        _grpcClient = grpcClient;
        _topic = topic;
        _options = options;
        _logger = logger ?? NullLogger<Consumer>.Instance;
    }

    public async Task ConsumeAsync(
        Func<QueueTiMessage, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var concurrency = Math.Max(1, _options.Concurrency);
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);

        var backoff = _minBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var request = new SubscribeRequest
                {
                    Topic = _topic,
                    ConsumerGroup = _options.ConsumerGroup
                };

                if (_options.VisibilityTimeoutSeconds.HasValue)
                    request.VisibilityTimeoutSeconds = _options.VisibilityTimeoutSeconds.Value;

                using var stream = _grpcClient.Subscribe(request, cancellationToken: ct);

                var resetBackoff = true;
                await foreach (var response in stream.ResponseStream.ReadAllAsync(ct))
                {
                    if (resetBackoff) { backoff = _minBackoff; resetBackoff = false; }

                    var message = QueueTiMessage.FromSubscribeResponse(response, _options.ConsumerGroup, _grpcClient);

                    await semaphore.WaitAsync(ct);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await handler(message, ct);
                            await message.AckAsync(CancellationToken.None);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // Propagate graceful shutdown without nacking.
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Handler failed for message {MessageId}; sending Nack.", message.Id);
                            try
                            {
                                await message.NackAsync(ex.Message, CancellationToken.None);
                            }
                            catch (Exception nackEx)
                            {
                                _logger.LogError(nackEx, "Nack failed for message {MessageId}.", message.Id);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex)
            {
                _logger.LogWarning(ex, "Subscribe stream ended with gRPC error; reconnecting in {Backoff}.", backoff);
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                backoff = Min(backoff * 2, _maxBackoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in subscribe loop; reconnecting in {Backoff}.", backoff);
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                backoff = Min(backoff * 2, _maxBackoff);
            }
        }
    }

    public async Task ConsumeBatchAsync(
        int batchSize,
        Func<IReadOnlyList<QueueTiMessage>, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var backoff = _minBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var request = new BatchDequeueRequest
                {
                    Topic = _topic,
                    Count = (uint)batchSize,
                    ConsumerGroup = _options.ConsumerGroup
                };

                if (_options.VisibilityTimeoutSeconds.HasValue)
                    request.VisibilityTimeoutSeconds = _options.VisibilityTimeoutSeconds.Value;

                var response = await _grpcClient.BatchDequeueAsync(request, cancellationToken: ct);

                if (response.Messages.Count == 0)
                {
                    await Task.Delay(backoff, ct);
                    backoff = Min(backoff * 2, _maxBackoff);
                    continue;
                }

                backoff = _minBackoff;

                var messages = response.Messages
                    .Select(m => QueueTiMessage.FromDequeueResponse(m, _options.ConsumerGroup, _grpcClient))
                    .ToList();

                await handler(messages, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex)
            {
                _logger.LogWarning(ex, "BatchDequeue failed with gRPC error; retrying in {Backoff}.", backoff);
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                backoff = Min(backoff * 2, _maxBackoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in batch consume loop; retrying in {Backoff}.", backoff);
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                backoff = Min(backoff * 2, _maxBackoff);
            }
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
