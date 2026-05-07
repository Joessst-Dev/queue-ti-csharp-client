using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;

namespace QueueTi;

public sealed class QueueTiClientOptions
{
    public string? BearerToken { get; set; }
    public Func<CancellationToken, Task<string>>? TokenRefresher { get; set; }
    public bool Insecure { get; set; }

    /// <summary>
    /// TLS configuration. Mutually exclusive with <see cref="Insecure"/>.
    /// When null and <see cref="Insecure"/> is false, the system trust store is used.
    /// </summary>
    public TlsOptions? Tls { get; set; }

    public Action<Grpc.Net.Client.GrpcChannelOptions>? ConfigureChannel { get; set; }

    public Action<IHttpClientBuilder>? ConfigureHttpClientBuilder { get; set; }
}
