using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueueTi.Pb;

namespace QueueTi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQueueTiClient(
        this IServiceCollection services,
        string address,
        Action<QueueTiClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new QueueTiClientOptions();
        configure(options);

        var grpcClientBuilder = services.AddGrpcClient<QueueService.QueueServiceClient>(o => o.Address = new Uri(address));

        options.ConfigureHttpClientBuilder?.Invoke(grpcClientBuilder);

        TokenStore? sharedTokenStore = null;
        if (options.BearerToken is not null)
        {
            sharedTokenStore = new TokenStore(options.BearerToken);
            var interceptor = new BearerTokenInterceptor(sharedTokenStore);
            grpcClientBuilder.AddInterceptor(() => interceptor);
        }

        var capturedTokenStore = sharedTokenStore;
        services.AddSingleton(sp =>
        {
            var grpcClient = sp.GetRequiredService<QueueService.QueueServiceClient>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new QueueTiClient(grpcClient, options, tokenStore: capturedTokenStore, ownedChannel: null, loggerFactory);
        });

        return services;
    }
}
