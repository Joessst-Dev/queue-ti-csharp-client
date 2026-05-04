using Grpc.Core;
using Grpc.Core.Interceptors;

namespace QueueTi;

internal sealed class BearerTokenInterceptor : Interceptor
{
    private readonly TokenStore _store;

    internal BearerTokenInterceptor(TokenStore store) => _store = store;

    private Metadata AddAuth(Metadata? headers)
    {
        var meta = new Metadata();
        if (headers is not null)
            foreach (var entry in headers)
                meta.Add(entry);
        meta.Add("authorization", "Bearer " + _store.Get());
        return meta;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var ctx = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithHeaders(AddAuth(context.Options.Headers)));
        return continuation(request, ctx);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var ctx = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithHeaders(AddAuth(context.Options.Headers)));
        return continuation(request, ctx);
    }
}
