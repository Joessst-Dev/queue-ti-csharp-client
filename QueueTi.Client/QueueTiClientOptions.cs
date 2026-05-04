namespace QueueTi;

public sealed class QueueTiClientOptions
{
    public string? BearerToken { get; set; }
    public Func<CancellationToken, Task<string>>? TokenRefresher { get; set; }
    public bool Insecure { get; set; }

    public Action<Grpc.Net.Client.GrpcChannelOptions>? ConfigureChannel { get; set; }
}
