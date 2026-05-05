using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using QueueTi.Extensions;

namespace QueueTi.Aspire;

public static class AspireQueueTiClientExtensions
{
    private const string DefaultConfigSectionName = "QueueTi";

    public static void AddQueueTiClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<QueueTiClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var settings = new QueueTiClientSettings();

        builder.Configuration
            .GetSection($"{DefaultConfigSectionName}:{connectionName}")
            .Bind(settings);

        var connectionString = builder.Configuration.GetConnectionString(connectionName);
        if (connectionString is not null)
        {
            settings.ConnectionString = connectionString;
        }

        configureSettings?.Invoke(settings);

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                $"A connection string for '{connectionName}' was not found. " +
                $"Ensure the resource is referenced in the AppHost or set ConnectionStrings:{connectionName} in configuration.");
        }

        builder.Services.AddQueueTiClient(settings.ConnectionString, opts =>
        {
            opts.Insecure = settings.ConnectionString.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            opts.BearerToken = settings.BearerToken;
            opts.TokenRefresher = settings.TokenRefresher;
        });

        if (!settings.DisableHealthChecks && settings.HttpUrl is not null)
        {
            var healthUrl = new Uri($"{settings.HttpUrl.TrimEnd('/')}/healthz");
            builder.Services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    $"QueueTi_{connectionName}",
                    _ => new QueueTiHealthCheck(healthUrl),
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["live", "queueti"]));
        }

        if (!settings.DisableTracing)
        {
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddGrpcClientInstrumentation());
        }
    }

}
