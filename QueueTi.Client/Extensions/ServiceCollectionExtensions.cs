using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueueTi.Pb;

namespace QueueTi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQueueTiAdminClient(
        this IServiceCollection services,
        string baseUrl,
        Action<QueueTiClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new QueueTiClientOptions();
        configure(options);

        TokenStore? sharedTokenStore = null;
        if (options.BearerToken is not null)
        {
            sharedTokenStore = new TokenStore(options.BearerToken);
        }

        var capturedStore = sharedTokenStore;
        var clientName = $"QueueTiAdmin:{baseUrl}";
        var httpClientBuilder = services.AddHttpClient(clientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
        });

        if (options.Insecure)
        {
            httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        if (capturedStore is not null)
        {
            httpClientBuilder.AddHttpMessageHandler(() => new BearerTokenHandler(capturedStore));
        }

        options.ConfigureHttpClientBuilder?.Invoke(httpClientBuilder);

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var httpClient = factory.CreateClient(clientName);
            return new AdminClient(httpClient, options, capturedStore, ownsHttpClient: false, loggerFactory);
        });

        return services;
    }

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

        if (options.Insecure)
        {
            grpcClientBuilder.ConfigureChannel(opts =>
                opts.Credentials = Grpc.Core.ChannelCredentials.Insecure);
        }

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
