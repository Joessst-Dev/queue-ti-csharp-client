using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueueTi;
using QueueTi.Extensions;

namespace QueueTi.Client.Tests;

public sealed class AdminClientTests
{
    private static async Task<(AdminClient client, IHost host)> BuildAsync(
        Action<IEndpointRouteBuilder> configureRoutes)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(wb =>
            {
                wb.UseTestServer();
                wb.ConfigureServices(s => s.AddRouting());
                wb.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(configureRoutes);
                });
            })
            .StartAsync();

        var httpClient = host.GetTestServer().CreateClient();
        return (new AdminClient(httpClient), host);
    }

    [Fact]
    public async Task ListTopicConfigsAsync_GivenOkResponse_ShouldReturnConfigs()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/topic-configs", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    """[{"topic":"orders","replayable":true,"max_retries":3}]""");
            });
        });

        // Act (When)
        var result = await client.ListTopicConfigsAsync();

        // Assert (Then)
        Assert.Single(result);
        Assert.Equal("orders", result[0].Topic);
        Assert.True(result[0].Replayable);
        Assert.Equal(3, result[0].MaxRetries);

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task UpsertTopicConfigAsync_GivenOkResponse_ShouldSendConfigAndReturnUpdated()
    {
        // Arrange (Given)
        string? capturedBody = null;
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapPut("/api/topic-configs/{topic}", async ctx =>
            {
                capturedBody = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    """{"topic":"orders","replayable":false,"max_retries":5}""");
            });
        });

        var config = new TopicConfig("orders", Replayable: false, MaxRetries: 5);

        // Act (When)
        var result = await client.UpsertTopicConfigAsync("orders", config);

        // Assert (Then)
        Assert.Equal("orders", result.Topic);
        Assert.False(result.Replayable);
        Assert.Equal(5, result.MaxRetries);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.Equal("orders", doc.RootElement.GetProperty("topic").GetString());
        Assert.False(doc.RootElement.GetProperty("replayable").GetBoolean());
        Assert.Equal(5, doc.RootElement.GetProperty("max_retries").GetInt32());

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task DeleteTopicConfigAsync_GivenNoContent_ShouldSucceed()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapDelete("/api/topic-configs/{topic}", ctx =>
            {
                ctx.Response.StatusCode = 204;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await client.DeleteTopicConfigAsync("orders");

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task DeleteTopicConfigAsync_GivenNotFound_ShouldThrowNotFoundException()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapDelete("/api/topic-configs/{topic}", ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<QueueTiNotFoundException>(
            () => client.DeleteTopicConfigAsync("missing-topic"));

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task ListTopicSchemasAsync_GivenOkResponse_ShouldReturnSchemas()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/topic-schemas", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    """[{"topic":"orders","schema_json":"{}","version":1,"updated_at":"2024-01-01T00:00:00Z"}]""");
            });
        });

        // Act (When)
        var result = await client.ListTopicSchemasAsync();

        // Assert (Then)
        Assert.Single(result);
        Assert.Equal("orders", result[0].Topic);
        Assert.Equal("{}", result[0].SchemaJson);
        Assert.Equal(1, result[0].Version);

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task GetTopicSchemaAsync_GivenOkResponse_ShouldReturnSchema()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/topic-schemas/{topic}", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    """{"topic":"orders","schema_json":"{}","version":2,"updated_at":"2024-06-01T00:00:00Z"}""");
            });
        });

        // Act (When)
        var result = await client.GetTopicSchemaAsync("orders");

        // Assert (Then)
        Assert.Equal("orders", result.Topic);
        Assert.Equal(2, result.Version);

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task GetTopicSchemaAsync_GivenNotFound_ShouldThrowNotFoundException()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/topic-schemas/{topic}", ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<QueueTiNotFoundException>(
            () => client.GetTopicSchemaAsync("missing-topic"));

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task UpsertTopicSchemaAsync_GivenOkResponse_ShouldSendSchemaJsonAndReturnSchema()
    {
        // Arrange (Given)
        string? capturedBody = null;
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapPut("/api/topic-schemas/{topic}", async ctx =>
            {
                capturedBody = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    """{"topic":"orders","schema_json":"{\"type\":\"object\"}","version":3,"updated_at":"2024-09-01T00:00:00Z"}""");
            });
        });

        const string schema = """{"type":"object"}""";

        // Act (When)
        var result = await client.UpsertTopicSchemaAsync("orders", schema);

        // Assert (Then)
        Assert.Equal("orders", result.Topic);
        Assert.Equal(3, result.Version);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.Equal(schema, doc.RootElement.GetProperty("schema_json").GetString());

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task UpsertTopicSchemaAsync_GivenConflict_ShouldThrowConflictException()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapPut("/api/topic-schemas/{topic}", ctx =>
            {
                ctx.Response.StatusCode = 409;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<QueueTiConflictException>(
            () => client.UpsertTopicSchemaAsync("orders", "{}"));

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task DeleteTopicSchemaAsync_GivenNoContent_ShouldSucceed()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapDelete("/api/topic-schemas/{topic}", ctx =>
            {
                ctx.Response.StatusCode = 204;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await client.DeleteTopicSchemaAsync("orders");

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task ListConsumerGroupsAsync_GivenOkResponse_ShouldReturnGroups()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/topics/{topic}/consumer-groups", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""["group-a","group-b"]""");
            });
        });

        // Act (When)
        var result = await client.ListConsumerGroupsAsync("orders");

        // Assert (Then)
        Assert.Equal(2, result.Count);
        Assert.Contains("group-a", result);
        Assert.Contains("group-b", result);

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task RegisterConsumerGroupAsync_GivenCreated_ShouldSendGroupName()
    {
        // Arrange (Given)
        string? capturedBody = null;
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapPost("/api/topics/{topic}/consumer-groups", async ctx =>
            {
                capturedBody = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                ctx.Response.StatusCode = 201;
            });
        });

        // Act (When)
        await client.RegisterConsumerGroupAsync("orders", "group-c");

        // Assert (Then)
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.Equal("group-c", doc.RootElement.GetProperty("consumer_group").GetString());

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task UnregisterConsumerGroupAsync_GivenNoContent_ShouldSucceed()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapDelete("/api/topics/{topic}/consumer-groups/{group}", ctx =>
            {
                ctx.Response.StatusCode = 204;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await client.UnregisterConsumerGroupAsync("orders", "group-a");

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task StatsAsync_GivenOkResponse_ShouldReturnStats()
    {
        // Arrange (Given)
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/stats", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    """[{"topic":"orders","status":"active","count":42}]""");
            });
        });

        // Act (When)
        var result = await client.StatsAsync();

        // Assert (Then)
        Assert.Single(result);
        Assert.Equal("orders", result[0].Topic);
        Assert.Equal("active", result[0].Status);
        Assert.Equal(42, result[0].Count);

        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task AdminClient_GivenBearerToken_ShouldSendAuthorizationHeader()
    {
        // Arrange (Given)
        string? capturedAuth = null;
        var (client, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/stats", async ctx =>
            {
                capturedAuth = ctx.Request.Headers.Authorization.ToString();
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("[]");
            });
        });

        // Swap the client for one with a token
        client.Dispose();
        const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjo5OTk5OTk5OTk5fQ.sig";
        var handler = new BearerTokenHandler(new TokenStore(token)) { InnerHandler = host.GetTestServer().CreateHandler() };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var tokenClient = new AdminClient(httpClient);

        // Act (When)
        await tokenClient.StatsAsync();

        // Assert (Then)
        Assert.Equal($"Bearer {token}", capturedAuth);

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task AddQueueTiAdminClient_GivenValidConfig_ShouldResolveSingletonClient()
    {
        // Arrange (Given)
        var (_, host) = await BuildAsync(ep => { });
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddQueueTiAdminClient("http://localhost", opts =>
        {
            opts.ConfigureHttpClientBuilder = b =>
                b.ConfigurePrimaryHttpMessageHandler(() => host.GetTestServer().CreateHandler());
        });

        // Act (When)
        await using var provider = services.BuildServiceProvider();
        var client1 = provider.GetRequiredService<AdminClient>();
        var client2 = provider.GetRequiredService<AdminClient>();

        // Assert (Then)
        Assert.NotNull(client1);
        Assert.Same(client1, client2);

        await host.StopAsync();
        host.Dispose();
    }
}
