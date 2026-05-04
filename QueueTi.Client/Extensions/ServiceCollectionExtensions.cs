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

        services.AddGrpcClient<QueueService.QueueServiceClient>(o => o.Address = new Uri(address));

        services.AddSingleton(sp =>
        {
            var grpcClient = sp.GetRequiredService<QueueService.QueueServiceClient>();
            var logger = sp.GetService<ILogger<QueueTiClient>>();
            return new QueueTiClient(grpcClient, options, logger);
        });

        return services;
    }
}
