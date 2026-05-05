using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QueueTi.Aspire;

internal sealed class QueueTiHealthCheck : IHealthCheck
{
    private static readonly HttpClient _http = new();
    private readonly Uri _healthUrl;

    public QueueTiHealthCheck(Uri healthUrl)
    {
        _healthUrl = healthUrl;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync(_healthUrl, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }
}
