using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.QueueTi;

public static class QueueTiResourceBuilderExtensions
{
    private const string ContainerImage = "ghcr.io/joessst-dev/queue-ti";
    private const string ContainerTag = "latest";
    private const int DefaultGrpcPort = 50051;
    private const int DefaultHttpPort = 8080;

    public static IResourceBuilder<QueueTiResource> AddQueueTi(
        this IDistributedApplicationBuilder builder,
        string name,
        int? grpcPort = null,
        int? httpPort = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new QueueTiResource(name);

        return builder
            .AddResource(resource)
            .WithImage(ContainerImage, ContainerTag)
            .WithImageRegistry("ghcr.io")
            .WithEndpoint(
                targetPort: DefaultGrpcPort,
                port: grpcPort,
                name: QueueTiResource.GrpcEndpointName,
                scheme: "http")
            .WithEndpoint(
                targetPort: DefaultHttpPort,
                port: httpPort,
                name: QueueTiResource.HttpEndpointName,
                scheme: "http")
            .WithEnvironment(QueueTiResource.ServerPortEnv, DefaultGrpcPort.ToString())
            .WithEnvironment(QueueTiResource.HttpPortEnv, DefaultHttpPort.ToString());
    }

    public static IResourceBuilder<QueueTiResource> WithDatabase<T>(
        this IResourceBuilder<QueueTiResource> builder,
        IResourceBuilder<T> database)
        where T : IResourceWithConnectionString
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        return builder
            .WithEnvironment(async context =>
            {
                var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken);

                if (connectionString is null)
                {
                    return;
                }

                // Parse host, port, username, password, dbname from connection string
                var parts = connectionString
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

                if (parts.TryGetValue("host", out var host))
                {
                    context.EnvironmentVariables[QueueTiResource.DbHostEnv] = host;
                }
                if (parts.TryGetValue("port", out var port))
                {
                    context.EnvironmentVariables[QueueTiResource.DbPortEnv] = port;
                }
                if (parts.TryGetValue("username", out var user))
                {
                    context.EnvironmentVariables[QueueTiResource.DbUserEnv] = user;
                }
                if (parts.TryGetValue("password", out var password))
                {
                    context.EnvironmentVariables[QueueTiResource.DbPasswordEnv] = password;
                }
                if (parts.TryGetValue("database", out var dbName))
                {
                    context.EnvironmentVariables[QueueTiResource.DbNameEnv] = dbName;
                }

                context.EnvironmentVariables[QueueTiResource.DbSslModeEnv] = "disable";
            });
    }

    public static IResourceBuilder<QueueTiResource> WithAuthentication(
        this IResourceBuilder<QueueTiResource> builder,
        string username,
        IResourceBuilder<ParameterResource> password,
        IResourceBuilder<ParameterResource> jwtSecret)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(jwtSecret);

        return builder
            .WithEnvironment("QUEUETI_AUTH_ENABLED", "true")
            .WithEnvironment("QUEUETI_AUTH_USERNAME", username)
            .WithEnvironment("QUEUETI_AUTH_PASSWORD", password)
            .WithEnvironment("QUEUETI_AUTH_JWT_SECRET", jwtSecret);
    }

    public static IResourceBuilder<QueueTiResource> WithLogLevel(
        this IResourceBuilder<QueueTiResource> builder,
        string level = "info")
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithEnvironment("QUEUETI_LOG_LEVEL", level);
    }
}
