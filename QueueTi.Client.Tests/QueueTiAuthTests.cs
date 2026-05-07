using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueueTi;

namespace QueueTi.Client.Tests;

public sealed class QueueTiAuthTests
{
    private static async Task<(HttpClient http, IHost host)> BuildAsync(
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

        return (host.GetTestServer().CreateClient(), host);
    }

    // ── FetchTokenAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchTokenAsync_GivenValidCredentials_ShouldReturnToken()
    {
        // Arrange (Given)
        string? capturedUsername = null;
        string? capturedPassword = null;

        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapPost("/api/auth/login", async ctx =>
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                capturedUsername = doc.RootElement.GetProperty("username").GetString();
                capturedPassword = doc.RootElement.GetProperty("password").GetString();

                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"token":"test-jwt-token"}""");
            });
        });

        // Act (When)
        var token = await QueueTiAuth.FetchTokenAsync(http, "admin", "secret");

        // Assert (Then)
        Assert.Equal("test-jwt-token", token);
        Assert.Equal("admin", capturedUsername);
        Assert.Equal("secret", capturedPassword);

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task FetchTokenAsync_GivenUnauthorizedResponse_ShouldThrow()
    {
        // Arrange (Given)
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapPost("/api/auth/login", ctx =>
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            });
        });

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<HttpRequestException>(
            () => QueueTiAuth.FetchTokenAsync(http, "admin", "wrong"));

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task FetchTokenAsync_GivenResponseWithoutTokenField_ShouldThrowInvalidOperationException()
    {
        // Arrange (Given)
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapPost("/api/auth/login", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"access_token":"wrong-field-name"}""");
            });
        });

        // Act & Assert (When & Then)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => QueueTiAuth.FetchTokenAsync(http, "admin", "admin"));
        Assert.Contains("token", ex.Message);

        await host.StopAsync();
        host.Dispose();
    }

    // ── LoginAsync (session) ────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_GivenAuthDisabled_ShouldReturnNoOpSession()
    {
        // Arrange (Given)
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/auth/status", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"auth_required":false}""");
            });
        });

        // Act (When)
        var session = await QueueTiAuth.LoginAsync(http, "admin", "secret",
            refresher: _ => Task.FromResult("should-not-be-called"));

        // Assert (Then)
        Assert.Null(session.Token);
        Assert.NotNull(session.RefreshAsync);
        var refreshed = await session.RefreshAsync(CancellationToken.None);
        Assert.Equal(string.Empty, refreshed);

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task LoginAsync_GivenAuthEnabled_ShouldReturnSessionWithToken()
    {
        // Arrange (Given)
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/auth/status", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"auth_required":true}""");
            });

            ep.MapPost("/api/auth/login", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"token":"test-jwt-token"}""");
            });
        });

        // Act (When)
        var session = await QueueTiAuth.LoginAsync(http, "admin", "secret",
            refresher: async ct => await QueueTiAuth.FetchTokenAsync(http, "admin", "secret", ct));

        // Assert (Then)
        Assert.Equal("test-jwt-token", session.Token);
        Assert.NotNull(session.RefreshAsync);

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task LoginAsync_GivenAuthEnabled_RefreshAsyncShouldFetchNewToken()
    {
        // Arrange (Given)
        var callCount = 0;
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/auth/status", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"auth_required":true}""");
            });

            ep.MapPost("/api/auth/login", async ctx =>
            {
                callCount++;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync($$$"""{"token":"token-call-{{{callCount}}}"}""");
            });
        });

        var session = await QueueTiAuth.LoginAsync(http, "admin", "secret",
            refresher: async ct => await QueueTiAuth.FetchTokenAsync(http, "admin", "secret", ct));

        // Act (When)
        var refreshedToken = await session.RefreshAsync(CancellationToken.None);

        // Assert (Then)
        Assert.Equal("token-call-1", session.Token);
        Assert.Equal("token-call-2", refreshedToken);

        await host.StopAsync();
        host.Dispose();
    }

    // ── GetAuthRequiredAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuthRequiredAsync_GivenAuthEnabled_ShouldReturnTrue()
    {
        // Arrange (Given)
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/auth/status", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"auth_required":true}""");
            });
        });

        // Act (When)
        var result = await QueueTiAuth.GetAuthRequiredAsync(http);

        // Assert (Then)
        Assert.True(result);

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task GetAuthRequiredAsync_GivenAuthDisabled_ShouldReturnFalse()
    {
        // Arrange (Given)
        var (http, host) = await BuildAsync(ep =>
        {
            ep.MapGet("/api/auth/status", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"auth_required":false}""");
            });
        });

        // Act (When)
        var result = await QueueTiAuth.GetAuthRequiredAsync(http);

        // Assert (Then)
        Assert.False(result);

        await host.StopAsync();
        host.Dispose();
    }
}
