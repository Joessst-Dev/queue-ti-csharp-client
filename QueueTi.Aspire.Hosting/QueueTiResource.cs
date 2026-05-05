using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.QueueTi;

public sealed class QueueTiResource : ContainerResource, IResourceWithConnectionString
{
    internal const string GrpcEndpointName = "grpc";
    internal const string HttpEndpointName = "http";

    internal const string DbHostEnv = "QUEUETI_DB_HOST";
    internal const string DbPortEnv = "QUEUETI_DB_PORT";
    internal const string DbUserEnv = "QUEUETI_DB_USER";
    internal const string DbPasswordEnv = "QUEUETI_DB_PASSWORD";
    internal const string DbNameEnv = "QUEUETI_DB_NAME";
    internal const string DbSslModeEnv = "QUEUETI_DB_SSLMODE";
    internal const string ServerPortEnv = "QUEUETI_SERVER_PORT";
    internal const string HttpPortEnv = "QUEUETI_SERVER_HTTP_PORT";

    internal const string AuthEnabledEnv = "QUEUETI_AUTH_ENABLED";
    internal const string AuthUsernameEnv = "QUEUETI_AUTH_USERNAME";
    internal const string AuthPasswordEnv = "QUEUETI_AUTH_PASSWORD";
    internal const string AuthJwtSecretEnv = "QUEUETI_AUTH_JWT_SECRET";

    internal const string RedisHostEnv = "QUEUETI_REDIS_HOST";
    internal const string RedisPortEnv = "QUEUETI_REDIS_PORT";
    internal const string RedisPasswordEnv = "QUEUETI_REDIS_PASSWORD";
    internal const string RedisTlsEnabledEnv = "QUEUETI_REDIS_TLS_ENABLED";

    public QueueTiResource(string name) : base(name)
    {
    }

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{new EndpointReference(this, GrpcEndpointName)}");
}
