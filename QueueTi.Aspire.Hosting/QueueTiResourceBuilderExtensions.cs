using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.QueueTi;

public static class QueueTiResourceBuilderExtensions
{
    private const string ContainerImage = "ghcr.io/joessst-dev/queue-ti";
    private const int DefaultGrpcPort = 50051;
    private const int DefaultHttpPort = 8080;
    private const int DefaultRedisPort = 6379;

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

        // Resolve the container that hosts the database (the server, not the database object itself).
        // GetValueAsync() returns the host-side mapped address, which is unreachable from inside another
        // container. DCP registers each container on a shared Docker network using the resource name as
        // the DNS alias, so we use that name + the internal target port instead.
        ContainerResource? serverContainer = database.Resource switch
        {
            IResourceWithParent { Parent: ContainerResource c } => c,
            ContainerResource c => c,
            _ => null
        };
        EndpointAnnotation? serverEndpoint = serverContainer?.Annotations
            .OfType<EndpointAnnotation>()
            .FirstOrDefault();

        // IResourceBuilder<T> is declared covariant (out T), so this cast is safe
        return builder
            .WaitFor((IResourceBuilder<IResource>)database)
            .WithEnvironment(async context =>
            {
                var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken)
                    ?? throw new DistributedApplicationException(
                        $"Could not resolve connection string for database resource '{database.Resource.Name}'.");

                // Parse username, password, dbname from the Npgsql semicolon-delimited connection string.
                // Host and port are overridden below for container-to-container scenarios.
                var parts = connectionString
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

                if (!parts.ContainsKey("username") && !parts.ContainsKey("password"))
                {
                    throw new DistributedApplicationException(
                        $"Could not parse connection string for '{database.Resource.Name}'. " +
                        "Expected Npgsql semicolon-delimited Key=Value format (e.g. Host=...;Username=...;Password=...).");
                }

                if (serverContainer is not null)
                {
                    context.EnvironmentVariables[QueueTiResource.DbHostEnv] = serverContainer.Name;
                    context.EnvironmentVariables[QueueTiResource.DbPortEnv] =
                        (serverEndpoint?.TargetPort ?? 5432).ToString();
                }
                else
                {
                    if (parts.TryGetValue("host", out var host))
                        context.EnvironmentVariables[QueueTiResource.DbHostEnv] = host;
                    if (parts.TryGetValue("port", out var port))
                        context.EnvironmentVariables[QueueTiResource.DbPortEnv] = port;
                }

                if (parts.TryGetValue("username", out var user))
                    context.EnvironmentVariables[QueueTiResource.DbUserEnv] = user;
                if (parts.TryGetValue("password", out var password))
                    context.EnvironmentVariables[QueueTiResource.DbPasswordEnv] = password;
                if (parts.TryGetValue("database", out var dbName))
                    context.EnvironmentVariables[QueueTiResource.DbNameEnv] = dbName;

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

        // Same container-to-container addressing fix as WithNpgsqlDatabase — use the resource name
        // as the Docker network DNS alias and the internal target port, not the host-mapped port.
        ContainerResource? redisContainer = redis.Resource switch
        {
            ContainerResource c => c,
            IResourceWithParent { Parent: ContainerResource c } => c,
            _ => null
        };
        EndpointAnnotation? redisEndpoint = redisContainer?.Annotations
            .OfType<EndpointAnnotation>()
            .FirstOrDefault();

        // IResourceBuilder<T> is declared covariant (out T), so this cast is safe
        return builder
            .WaitFor((IResourceBuilder<IResource>)redis)
            .WithEnvironment(async context =>
            {
                var connectionString = await redis.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken)
                    ?? throw new DistributedApplicationException(
                        $"Could not resolve connection string for Redis resource '{redis.Resource.Name}'.");

                // Parse StackExchange.Redis format: host:port[,option=value,...]
                // Use a loop so duplicate keys don't throw — last value wins
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var segment in connectionString.Split(',', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                {
                    var kv = segment.Split('=', 2);
                    if (kv.Length == 2)
                        options[kv[0].Trim()] = kv[1].Trim();
                }

                if (redisContainer is not null)
                {
                    context.EnvironmentVariables[QueueTiResource.RedisHostEnv] = redisContainer.Name;
                    context.EnvironmentVariables[QueueTiResource.RedisPortEnv] =
                        (redisEndpoint?.TargetPort ?? DefaultRedisPort).ToString();
                }
                else
                {
                    var firstSegment = connectionString.Split(',')[0];
                    var colonIdx = firstSegment.LastIndexOf(':');
                    context.EnvironmentVariables[QueueTiResource.RedisHostEnv] =
                        colonIdx >= 0 ? firstSegment[..colonIdx] : firstSegment;
                    context.EnvironmentVariables[QueueTiResource.RedisPortEnv] =
                        colonIdx >= 0 ? firstSegment[(colonIdx + 1)..] : DefaultRedisPort.ToString();
                }

                if (options.TryGetValue("password", out var password))
                    context.EnvironmentVariables[QueueTiResource.RedisPasswordEnv] = password;

                if (options.TryGetValue("ssl", out var ssl) && ssl.Equals("true", StringComparison.OrdinalIgnoreCase))
                    context.EnvironmentVariables[QueueTiResource.RedisTlsEnabledEnv] = "true";
            });
    }

    public static IResourceBuilder<QueueTiResource> WithReplicas(
        this IResourceBuilder<QueueTiResource> builder,
        int replicas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(replicas, 1);

        return builder.WithAnnotation(new ReplicaAnnotation(replicas), ResourceAnnotationMutationBehavior.Replace);
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
