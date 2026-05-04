using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueueTi.Pb;

namespace QueueTi;

public sealed class QueueTiClient : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan _refreshMinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _refreshMaxBackoff = TimeSpan.FromSeconds(60);
    // Refresh 60 seconds before token expiry so the transition is seamless.
    private static readonly TimeSpan _refreshLeadTime = TimeSpan.FromSeconds(60);

    private readonly QueueService.QueueServiceClient _grpcClient;
    private readonly QueueTiClientOptions _options;
    private readonly TokenStore? _tokenStore;
    private readonly GrpcChannel? _ownedChannel;
    private readonly ILogger<QueueTiClient> _logger;
    private readonly CancellationTokenSource _refreshCts = new();
    private readonly Task _refreshTask;

    public QueueTiClient(
        QueueService.QueueServiceClient grpcClient,
        QueueTiClientOptions options,
        ILogger<QueueTiClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(grpcClient);
        ArgumentNullException.ThrowIfNull(options);

        _grpcClient = grpcClient;
        _options = options;
        _logger = logger ?? NullLogger<QueueTiClient>.Instance;

        if (options.BearerToken is not null)
            _tokenStore = new TokenStore(options.BearerToken);

        _refreshTask = options.TokenRefresher is not null && _tokenStore is not null
            ? RunTokenRefreshLoopAsync(_refreshCts.Token)
            : Task.CompletedTask;
    }

    private QueueTiClient(
        GrpcChannel channel,
        QueueTiClientOptions options,
        ILogger<QueueTiClient>? logger)
        : this(new QueueService.QueueServiceClient(channel), options, logger)
    {
        _ownedChannel = channel;
    }

    public static QueueTiClient Create(
        string address,
        QueueTiClientOptions options,
        ILogger<QueueTiClient>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(options);

        var channelOptions = new GrpcChannelOptions();

        if (options.Insecure)
            channelOptions.Credentials = Grpc.Core.ChannelCredentials.Insecure;

        options.ConfigureChannel?.Invoke(channelOptions);

        var channel = GrpcChannel.ForAddress(address, channelOptions);
        return new QueueTiClient(channel, options, logger);
    }

    public Producer NewProducer() => new(_grpcClient);

    public Consumer NewConsumer(string topic, ConsumerOptions? opts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        return new Consumer(_grpcClient, topic, opts ?? new ConsumerOptions(), _logger as ILogger<Consumer>);
    }

    public void SetToken(string token)
    {
        if (_tokenStore is null)
            throw new InvalidOperationException(
                "Cannot set token: no TokenStore is configured. Provide a BearerToken in QueueTiClientOptions.");

        _tokenStore.Set(token);
    }

    private async Task RunTokenRefreshLoopAsync(CancellationToken ct)
    {
        var backoff = _refreshMinBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var expiry = _tokenStore!.GetExpiry();
                var delay = expiry - DateTimeOffset.UtcNow - _refreshLeadTime;

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                var newToken = await _options.TokenRefresher!(ct);
                _tokenStore.Set(newToken);
                _logger.LogInformation("Bearer token refreshed successfully.");
                backoff = _refreshMinBackoff;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token refresh failed; retrying in {Backoff}.", backoff);
                try
                {
                    await Task.Delay(backoff, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                backoff = backoff * 2 > _refreshMaxBackoff ? _refreshMaxBackoff : backoff * 2;
            }
        }
    }

    public void Dispose()
    {
        _refreshCts.Cancel();
        try
        {
            _refreshTask.GetAwaiter().GetResult();
        }
        catch
        {
            // refresh task is best-effort; do not propagate cancellation on dispose
        }

        _tokenStore?.Dispose();
        _refreshCts.Dispose();
        _ownedChannel?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _refreshCts.CancelAsync();
        try
        {
            await _refreshTask;
        }
        catch
        {
            // refresh task is best-effort; do not propagate cancellation on dispose
        }

        _tokenStore?.Dispose();
        _refreshCts.Dispose();

        if (_ownedChannel is not null)
            await _ownedChannel.ShutdownAsync();
    }
}
