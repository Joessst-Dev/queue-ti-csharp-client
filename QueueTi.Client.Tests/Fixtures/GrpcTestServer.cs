using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QueueTi.Client.Tests.Fixtures;

public sealed class GrpcTestServer : IAsyncDisposable
{
    public GrpcChannel Channel { get; }
    public QueueServiceFake Fake { get; }

    private readonly IHost _host;

    private GrpcTestServer(IHost host, GrpcChannel channel, QueueServiceFake fake)
    {
        _host = host;
        Channel = channel;
        Fake = fake;
    }

    public static async Task<GrpcTestServer> CreateAsync()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddGrpc();
                    services.AddSingleton<QueueServiceFake>();
                });
                webBuilder.Configure(app =>
                {
                    var routeBuilder = app as Microsoft.AspNetCore.Routing.IEndpointRouteBuilder;
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGrpcService<QueueServiceFake>());
                });
            });

        var host = await hostBuilder.StartAsync();

        var testServer = host.GetTestServer();
        var httpClient = testServer.CreateClient();
        httpClient.DefaultRequestVersion = new Version(2, 0);

        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        var fake = host.Services.GetRequiredService<QueueServiceFake>();

        return new GrpcTestServer(host, channel, fake);
    }

    public HttpClient CreateHttpClient()
    {
        var client = _host.GetTestServer().CreateClient();
        client.DefaultRequestVersion = new Version(2, 0);
        return client;
    }

    public HttpMessageHandler CreateHandler() =>
        _host.GetTestServer().CreateHandler();

    public async ValueTask DisposeAsync()
    {
        Channel.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
