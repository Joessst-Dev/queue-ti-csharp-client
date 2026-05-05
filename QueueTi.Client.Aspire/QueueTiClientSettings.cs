namespace QueueTi.Aspire;

public sealed class QueueTiClientSettings
{
    public string? ConnectionString { get; set; }
    public bool DisableHealthChecks { get; set; }
    public bool DisableTracing { get; set; }
    public string? BearerToken { get; set; }
    public Func<CancellationToken, Task<string>>? TokenRefresher { get; set; }
}
