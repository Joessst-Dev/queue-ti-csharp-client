using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.QueueTi;

public static class QueueTiResourceBuilderExtensions
{
    private const string ContainerImage = "ghcr.io/joessst-dev/queue-ti";
    private const int DefaultGrpcPort = 50051;
    private const int DefaultHttpPort = 8080;
    private const string DefaultRedisPort = "6379";

    public static IResourceBuilder<QueueTiResource> AddQueueTi(
        this IDistributedApplicationBuilder builder,
        string name,
        int? grpcPort = null,
        int? httpPort = null,
        string tag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new QueueTiResource(name);

        return builder
            .AddResource(resource)
            .WithImage(ContainerImage, tag)
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

    public static IResourceBuilder<QueueTiResource> WithNpgsqlDatabase<T>(
        this IResourceBuilder<QueueTiResource> builder,
        IResourceBuilder<T> database)
        where T : IResourceWithConnectionString
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        // IResourceBuilder<T> is declared covariant (out T), so this cast is safe
        return builder
            .WaitFor((IResourceBuilder<IResource>)database)
            .WithEnvironment(async context =>
            {
                var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken)
                    ?? throw new DistributedApplicationException(
                        $"Could not resolve connection string for database resource '{database.Resource.Name}'.");

                // Parse host, port, username, password, dbname from Npgsql semicolon-delimited connection string
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

    public static IResourceBuilder<QueueTiResource> WithRedis<T>(
        this IResourceBuilder<QueueTiResource> builder,
        IResourceBuilder<T> redis)
        where T : IResourceWithConnectionString
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(redis);

        // IResourceBuilder<T> is declared covariant (out T), so this cast is safe
        return builder
            .WaitFor((IResourceBuilder<IResource>)redis)
            .WithEnvironment(async context =>
            {
                var connectionString = await redis.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken)
                    ?? throw new DistributedApplicationException(
                        $"Could not resolve connection string for Redis resource '{redis.Resource.Name}'.");

                // Parse StackExchange.Redis format: host:port[,option=value,...]
                var firstSegment = connectionString.Split(',')[0];
                var colonIdx = firstSegment.LastIndexOf(':');
                var host = colonIdx >= 0 ? firstSegment[..colonIdx] : firstSegment;
                var port = colonIdx >= 0 ? firstSegment[(colonIdx + 1)..] : DefaultRedisPort;

                // Use a loop so duplicate keys don't throw — last value wins
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var segment in connectionString.Split(',', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                {
                    var kv = segment.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        options[kv[0].Trim()] = kv[1].Trim();
                    }
                }

                context.EnvironmentVariables[QueueTiResource.RedisHostEnv] = host;
                context.EnvironmentVariables[QueueTiResource.RedisPortEnv] = port;

                if (options.TryGetValue("password", out var password))
                {
                    context.EnvironmentVariables[QueueTiResource.RedisPasswordEnv] = password;
                }

                if (options.TryGetValue("ssl", out var ssl) && ssl.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    context.EnvironmentVariables[QueueTiResource.RedisTlsEnabledEnv] = "true";
                }
            });
    }

    public static IResourceBuilder<QueueTiResource> WithReplicas(
        this IResourceBuilder<QueueTiResource> builder,
        int replicas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(replicas, 1);

        return builder.WithAnnotation(new ReplicaAnnotation(replicas));
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
            .WithEnvironment(QueueTiResource.AuthEnabledEnv, "true")
            .WithEnvironment(QueueTiResource.AuthUsernameEnv, username)
            .WithEnvironment(QueueTiResource.AuthPasswordEnv, password)
            .WithEnvironment(QueueTiResource.AuthJwtSecretEnv, jwtSecret);
    }

    public static IResourceBuilder<QueueTiResource> WithLogLevel(
        this IResourceBuilder<QueueTiResource> builder,
        string level = "info")
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithEnvironment("QUEUETI_LOG_LEVEL", level);
    }
}
