namespace QueueTi;

public sealed class QueueTiClientOptions
{
    public string? BearerToken { get; set; }
    public Func<CancellationToken, Task<string>>? TokenRefresher { get; set; }
    public bool Insecure { get; set; }

    /// <summary>
    /// Optional callback to further configure the <see cref="Grpc.Net.Client.GrpcChannelOptions"/>
    /// when the channel is created internally via <see cref="QueueTiClient.Create"/>.
    /// </summary>
    public Action<Grpc.Net.Client.GrpcChannelOptions>? ConfigureChannel { get; set; }
}
