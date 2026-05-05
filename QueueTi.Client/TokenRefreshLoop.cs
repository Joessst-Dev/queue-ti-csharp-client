using Microsoft.Extensions.Logging;

namespace QueueTi;

internal static class TokenRefreshLoop
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LeadTime = TimeSpan.FromSeconds(60);

    internal static async Task RunAsync(
        TokenStore tokenStore,
        Func<CancellationToken, Task<string>> refresher,
        ILogger logger,
        CancellationToken ct)
    {
        var backoff = MinBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var expiry = tokenStore.GetExpiry();
                var delay = expiry - DateTimeOffset.UtcNow - LeadTime;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }

                var newToken = await refresher(ct);
                tokenStore.Set(newToken);
                logger.LogInformation("Bearer token refreshed successfully.");
                backoff = MinBackoff;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Token refresh failed; retrying in {Backoff}.", backoff);
                try
                {
                    await Task.Delay(backoff, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                var next = backoff * 2;
                backoff = next < MaxBackoff ? next : MaxBackoff;
            }
        }
    }
}
