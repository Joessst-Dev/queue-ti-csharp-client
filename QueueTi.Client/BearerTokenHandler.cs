using System.Net.Http.Headers;

namespace QueueTi;

internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly TokenStore _tokenStore;

    internal BearerTokenHandler(TokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenStore.Get());
        return base.SendAsync(request, ct);
    }
}
