using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueueTi.Pb;

namespace QueueTi.Aspire;

internal sealed class QueueTiHealthCheck : IHealthCheck
{
    private readonly QueueService.QueueServiceClient _client;

    public QueueTiHealthCheck(QueueService.QueueServiceClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // A zero-byte dequeue is the lightest possible round-trip to verify connectivity.
            await _client.BatchDequeueAsync(
                new BatchDequeueRequest { Topic = "__health__", Count = 0 },
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unauthenticated or StatusCode.PermissionDenied)
        {
            // Server is reachable but auth is required — still healthy from a connectivity standpoint.
            return HealthCheckResult.Healthy("Reachable (auth required).");
        }
        catch (RpcException ex)
        {
            return HealthCheckResult.Unhealthy(ex.Status.Detail, ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }
}
