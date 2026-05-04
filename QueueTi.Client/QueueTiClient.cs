using Grpc.Core.Interceptors;
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
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<QueueTiClient> _logger;
    private readonly CancellationTokenSource _refreshCts = new();
    private readonly Task _refreshTask;
    private int _disposed;

    public QueueTiClient(
        QueueService.QueueServiceClient grpcClient,
        QueueTiClientOptions options,
        ILoggerFactory? loggerFactory = null)
        : this(grpcClient, options, tokenStore: null, ownedChannel: null, loggerFactory)
    {
    }

    internal QueueTiClient(
        QueueService.QueueServiceClient grpcClient,
        QueueTiClientOptions options,
        TokenStore? tokenStore,
        GrpcChannel? ownedChannel,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(grpcClient);
        ArgumentNullException.ThrowIfNull(options);

        _grpcClient = grpcClient;
        _options = options;
        _ownedChannel = ownedChannel;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<QueueTiClient>();

        _tokenStore = tokenStore ?? (options.BearerToken is not null ? new TokenStore(options.BearerToken) : null);

        _refreshTask = options.TokenRefresher is not null && _tokenStore is not null
            ? RunTokenRefreshLoopAsync(_refreshCts.Token)
            : Task.CompletedTask;
    }

    public static QueueTiClient Create(
        string address,
        QueueTiClientOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(options);

        var channelOptions = new GrpcChannelOptions();

        if (options.Insecure)
            channelOptions.Credentials = Grpc.Core.ChannelCredentials.Insecure;

        options.ConfigureChannel?.Invoke(channelOptions);

        var channel = GrpcChannel.ForAddress(address, channelOptions);

        if (options.BearerToken is not null)
        {
            var store = new TokenStore(options.BearerToken);
            var invoker = channel.Intercept(new BearerTokenInterceptor(store));
            var grpcClient = new QueueService.QueueServiceClient(invoker);
            return new QueueTiClient(grpcClient, options, tokenStore: store, ownedChannel: channel, loggerFactory);
        }

        return new QueueTiClient(
            new QueueService.QueueServiceClient(channel),
            options,
            tokenStore: null,
            ownedChannel: channel,
            loggerFactory);
    }

    public Producer NewProducer() => new(_grpcClient);

    public Consumer NewConsumer(string topic, ConsumerOptions? opts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        return new Consumer(_grpcClient, topic, opts ?? new ConsumerOptions(), _loggerFactory.CreateLogger<Consumer>());
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

                var next = backoff * 2;
                backoff = next < _refreshMaxBackoff ? next : _refreshMaxBackoff;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _refreshCts.Cancel();
        try { _refreshTask.GetAwaiter().GetResult(); } catch { }

        if (_ownedChannel is not null)
        {
            _ownedChannel.ShutdownAsync().GetAwaiter().GetResult();
            _ownedChannel.Dispose();
        }

        _tokenStore?.Dispose();
        _refreshCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await _refreshCts.CancelAsync();
        try { await _refreshTask; } catch { }

        if (_ownedChannel is not null)
        {
            await _ownedChannel.ShutdownAsync();
            _ownedChannel.Dispose();
        }

        _tokenStore?.Dispose();
        _refreshCts.Dispose();
    }
}
