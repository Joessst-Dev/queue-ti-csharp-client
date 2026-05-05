# QueueTi C# Client

[![NuGet](https://img.shields.io/nuget/v/QueueTi.Client)](https://www.nuget.org/packages/QueueTi.Client)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A proto-first gRPC client library for the [QueueTi](https://github.com/Joessst-Dev/queue-ti) distributed message queue service. Targets `.NET 10.0`.

## Installation

```bash
dotnet add package QueueTi.Client
```

Or with the Package Manager:

```powershell
Install-Package QueueTi.Client
```

## Quick Start

### Create a client

```csharp
using QueueTi;

var client = QueueTiClient.Create("https://queue.example.com", new QueueTiClientOptions
{
    BearerToken = "your-jwt-token"  // optional
});
```

### Publish a message

```csharp
using System.Text;

var producer = client.NewProducer();

string messageId = await producer.PublishAsync(
    topic: "orders",
    payload: Encoding.UTF8.GetBytes("Hello, QueueTi!"),
    ct: CancellationToken.None
);
```

### Consume messages (streaming)

```csharp
var consumer = client.NewConsumer("orders", new ConsumerOptions
{
    ConsumerGroup = "billing"
});

await consumer.ConsumeAsync(async (msg, ct) =>
{
    Console.WriteLine($"Message {msg.Id}: {Encoding.UTF8.GetString(msg.Payload)}");
    // Handler automatically acks on success, nacks on exception.
}, ct);
```

### Clean up

```csharp
await client.DisposeAsync();
```

## Client Creation

### Factory method (recommended)

Use `QueueTiClient.Create()` to construct a client with a managed gRPC channel:

```csharp
var client = QueueTiClient.Create("https://queue.example.com", new QueueTiClientOptions
{
    BearerToken = "jwt-token",        // optional; enables Bearer auth
    Insecure = false,                 // set true for plaintext http:// endpoints
    ConfigureChannel = opts => {      // optional; configure GrpcChannelOptions
        opts.MaxReceiveMessageSize = 16 * 1024 * 1024;
    }
});
```

### Manual channel (advanced)

If you need full control over the gRPC channel, pass your own `QueueService.QueueServiceClient`. In this path you are responsible for wiring any interceptors — `BearerToken` is not automatically applied:

```csharp
var channel = GrpcChannel.ForAddress("https://queue.example.com");
var grpcClient = new QueueService.QueueServiceClient(channel);
var client = new QueueTiClient(grpcClient, new QueueTiClientOptions());
```

To add bearer auth on a manual channel, intercept the invoker yourself:

```csharp
var store = new TokenStore("jwt-token");
var invoker = channel.Intercept(new BearerTokenInterceptor(store));
var grpcClient = new QueueService.QueueServiceClient(invoker);
var client = new QueueTiClient(grpcClient, new QueueTiClientOptions());
```

## Dependency Injection (ASP.NET Core)

Register the client in your service container using `AddQueueTiClient()`:

```csharp
builder.Services.AddQueueTiClient("https://queue.example.com", opts =>
{
    opts.BearerToken = "initial-token";
    opts.TokenRefresher = async ct => await GetFreshTokenAsync(ct);
    opts.ConfigureHttpClientBuilder = httpBuilder =>
    {
        // Configure timeouts, delegating handlers, etc.
        httpBuilder.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
    };
});

// Inject QueueTiClient into your controllers or services
app.MapPost("/orders", async (QueueTiClient client) =>
{
    var producer = client.NewProducer();
    var id = await producer.PublishAsync("orders", orderPayload);
    return Results.Created($"/orders/{id}", new { id });
});
```

## .NET Aspire Integration

The `QueueTi.Aspire.Hosting` and `QueueTi.Client.Aspire` packages provide seamless integration with .NET Aspire orchestration for both AppHost and service projects.

### Installation

Each package targets a different project in your Aspire solution:

**AppHost project:**
```bash
dotnet add package QueueTi.Aspire.Hosting
```

**Service/worker project:**
```bash
dotnet add package QueueTi.Client.Aspire
```

### AppHost Setup

Use `AddQueueTi()` to register QueueTi as a distributed application resource in your AppHost:

```csharp
// Program.cs (Aspire AppHost project)
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("queueti-db");

var redis = builder.AddRedis("redis"); // optional — enables rate limiting

var queue = builder.AddQueueTi("queue")
    .WithReplicas(3)                   // optional — defaults to 1
    .WithNpgsqlDatabase(postgres)
    .WithRedis(redis)                  // optional — enables rate limiting
    .WithAuthentication(
        username: "admin",
        password: builder.AddParameter("queue-password", secret: true),
        jwtSecret: builder.AddParameter("queue-jwt-secret", secret: true))
    .WithLogLevel("info");

builder.AddProject<Projects.MyWorker>("worker")
    .WithReference(queue);

builder.Build().Run();
```

**Key builder methods:**

| Method | Purpose |
|--------|---------|
| `AddQueueTi(name, grpcPort?, httpPort?, tag?)` | Adds a QueueTi container resource. Pulls `ghcr.io/joessst-dev/queue-ti`. Endpoints: `grpc` (target 50051), `http` (target 8080). |
| `WithNpgsqlDatabase(database)` | Wires an Npgsql database resource. Sets `QUEUETI_DB_*` env vars and adds `WaitFor` dependency. |
| `WithRedis(redis)` | Wires an optional Redis resource for rate limiting. Sets `QUEUETI_REDIS_*` env vars and adds `WaitFor` dependency. |
| `WithReplicas(n)` | Runs `n` instances of QueueTi. All replicas share the same database and Redis resources. |
| `WithAuthentication(username, password, jwtSecret)` | Configures authentication. Sets `QUEUETI_AUTH_ENABLED` and related env vars from `ParameterResource` values. |
| `WithLogLevel(level)` | Sets `QUEUETI_LOG_LEVEL`. |

### Replicas

`WithReplicas(n)` starts `n` identical QueueTi container instances. Aspire automatically load-balances `WithReference` connections across them, so service projects receive a connection string that round-robins between replicas without any extra configuration.

All replicas share the same database and Redis resources — wire those once on the resource builder and each instance picks up the same env vars:

```csharp
var queue = builder.AddQueueTi("queue")
    .WithReplicas(3)
    .WithNpgsqlDatabase(postgres)
    .WithRedis(redis);
```

> **Note:** `WithReplicas` sets a fixed count at startup. For dynamic scaling based on load, configure scaling rules at the deployment target (Azure Container Apps, Kubernetes HPA) rather than in the AppHost.

### Service Project Setup

In your worker or web project, call `AddQueueTiClient()` to register the gRPC client:

```csharp
// Program.cs (Service project)
builder.AddQueueTiClient("queue");

var app = builder.Build();
app.Run();
```

The client automatically:
- Reads the connection string from `ConnectionStrings:queue` (set by Aspire).
- Registers `QueueTiClient` in DI (and the underlying `QueueService.QueueServiceClient`).
- Configures health checks (HTTP GET to `/healthz` on port 8080).
- Instruments outbound gRPC calls with OpenTelemetry tracing.

**With custom settings:**

```csharp
builder.AddQueueTiClient("queue", settings =>
{
    settings.DisableHealthChecks = true; // if health checks are managed separately
    settings.BearerToken = "your-jwt";   // if auth is enabled on the server
});
```

### QueueTiClientSettings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | `string?` | `null` | Explicit connection string. If not set, read from `ConnectionStrings:{connectionName}` or `QueueTi:{connectionName}` config. |
| `DisableHealthChecks` | `bool` | `false` | Disable automatic health check registration. |
| `DisableTracing` | `bool` | `false` | Disable OpenTelemetry instrumentation. |
| `BearerToken` | `string?` | `null` | Optional bearer token for authentication. |
| `TokenRefresher` | `Func<CancellationToken, Task<string>>?` | `null` | Optional callback to refresh the bearer token at runtime. |

### Health Checks

When `DisableHealthChecks` is false (default), the integration registers an HTTP health check that probes `GET /healthz` on the QueueTi service's HTTP port (default 8080). The check is registered under tags `live` and `queueti` and requires no authentication.

### Distributed Tracing

When `DisableTracing` is false (default), all outbound gRPC calls are instrumented using `OpenTelemetry.Instrumentation.GrpcNetClient`. Traces are exported via the Aspire telemetry pipeline.

## Publishing Messages

Create a producer and call `PublishAsync()`:

```csharp
var producer = client.NewProducer();

// Minimal publish
string id = await producer.PublishAsync("orders", payload, ct: ct);

// With routing key and metadata
string id = await producer.PublishAsync("orders", payload, new PublishOptions
{
    Key = "order-123",  // optional routing/ordering key
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "api",
        ["version"] = "v1"
    }
}, ct);
```

### PublishOptions

| Property | Type | Description |
|----------|------|-------------|
| `Key` | `string?` | Optional routing or ordering key for the message. |
| `Metadata` | `IReadOnlyDictionary<string, string>?` | Arbitrary string key-value pairs attached to the message. |

## Consuming Messages

### Streaming consumer (real-time processing)

Use `ConsumeAsync()` for continuous subscription with automatic acknowledgment:

```csharp
var consumer = client.NewConsumer("orders", new ConsumerOptions
{
    ConsumerGroup = "billing",
    Concurrency = 4,                 // process up to 4 messages in parallel
    VisibilityTimeoutSeconds = 30    // optional; override server default
});

await consumer.ConsumeAsync(async (msg, ct) =>
{
    var orderData = JsonSerializer.Deserialize<Order>(msg.Payload);
    await BillingService.ProcessAsync(orderData, ct);
    // Automatically acked on return; automatically nacked if an exception is thrown.
}, ct);
```

**Behavior:**
- The handler is invoked for each message as it arrives from the server.
- The message is automatically **acked** on successful handler completion.
- The message is automatically **nacked** with the exception message if the handler throws.
- Handler invocations are limited by `Concurrency` to control parallelism.
- The consumer reconnects with exponential backoff (500 ms → 30 s) on gRPC errors.
- Graceful shutdown (cancellation token) exits without nacking in-flight messages.

### Batch consumer (polling)

Use `ConsumeBatchAsync()` for periodic polling:

```csharp
var consumer = client.NewConsumer("orders");

await consumer.ConsumeBatchAsync(
    batchSize: 10,
    handler: async (messages, ct) =>
    {
        foreach (var msg in messages)
        {
            try
            {
                await ProcessMessageAsync(msg, ct);
                await msg.AckAsync(ct);  // must ack manually
            }
            catch (Exception ex)
            {
                await msg.NackAsync(ex.Message, ct);  // must nack manually
            }
        }
    },
    ct: ct
);
```

**Behavior:**
- Polls the server for up to `batchSize` messages at a time.
- If no messages are available, waits and retries with exponential backoff (500 ms → 30 s).
- You **must** explicitly call `AckAsync()` or `NackAsync()` for each message.
- Reconnects automatically on gRPC errors with exponential backoff.

### ConsumerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConsumerGroup` | `string` | `""` | Consumer group identity for coordinating consumers across instances. |
| `Concurrency` | `int` | `1` | Maximum concurrent handler invocations in streaming mode. Ignored in batch mode. |
| `VisibilityTimeoutSeconds` | `uint?` | `null` | Visibility timeout (in seconds) for dequeued messages. If null, uses server default. |

## Message Handling

### QueueTiMessage

Each message received by the consumer provides:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Unique message identifier. |
| `Topic` | `string` | Topic name. |
| `Payload` | `byte[]` | Message body as bytes. |
| `Metadata` | `IReadOnlyDictionary<string, string>` | Key-value metadata attached at publish time. |
| `CreatedAt` | `DateTimeOffset` | Server timestamp when the message was created. |
| `RetryCount` | `int` | Number of times this message has been retried. |

### Acknowledgment

```csharp
// Ack: message processed successfully
await msg.AckAsync(ct);

// Nack: processing failed; mark for retry
await msg.NackAsync("Database connection timeout", ct);
```

**In streaming mode**, acks and nacks are called automatically by the consumer:
- Ack on successful handler completion.
- Nack with the exception message on handler failure.

**In batch mode**, you must call `AckAsync()` or `NackAsync()` explicitly for each message.

## Bearer Token Authentication

### Static token

Provide a bearer token at client creation:

```csharp
var client = QueueTiClient.Create(address, new QueueTiClientOptions
{
    BearerToken = "eyJhbGc..."
});
```

The token is injected as an `Authorization: Bearer <token>` header on every request.

### Dynamic token refresh

For long-lived clients, provide a `TokenRefresher` to refresh the token before expiry:

```csharp
var client = QueueTiClient.Create(address, new QueueTiClientOptions
{
    BearerToken = "initial-token",
    TokenRefresher = async ct =>
    {
        var response = await httpClient.GetAsync("/auth/refresh", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var token = JsonDocument.Parse(json).RootElement.GetString("access_token");
        return token!;
    }
});
```

**Behavior:**
- The refresh task runs in the background and calls the refresher ~60 seconds before token expiry.
- On refresh success, the new token is stored and used for all subsequent requests.
- On refresh failure, the task retries with exponential backoff (5 s → 60 s) until success.
- Refresh runs until the client is disposed.

### Update token at runtime

```csharp
client.SetToken("new-jwt-token");  // thread-safe; immediate effect
```

Throws if no `BearerToken` was configured at creation time.

## Configuration Reference

### QueueTiClientOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BearerToken` | `string?` | `null` | Initial JWT for Bearer authentication. If set, enables automatic token injection and refresh capability. |
| `TokenRefresher` | `Func<CancellationToken, Task<string>>?` | `null` | Optional async callback to refresh the bearer token. Called ~60 s before token expiry. Requires `BearerToken` to be set. |
| `Insecure` | `bool` | `false` | Use `ChannelCredentials.Insecure` for plaintext `http://` connections. Disable TLS/SSL. |
| `ConfigureChannel` | `Action<GrpcChannelOptions>?` | `null` | Callback to configure `GrpcChannelOptions` before channel creation (e.g., message size limits). |
| `ConfigureHttpClientBuilder` | `Action<IHttpClientBuilder>?` | `null` | DI-only: callback to configure the `IHttpClientBuilder` (e.g., add delegating handlers, set timeouts). |

## Disposal

Both `Dispose()` and `DisposeAsync()` are supported and idempotent:

```csharp
// Synchronous
client.Dispose();

// Asynchronous (preferred)
await client.DisposeAsync();
```

Disposal:
1. Cancels the background token refresh task (if running).
2. Shuts down the managed gRPC channel (if created via `Create()`).
3. Can be called multiple times safely; subsequent calls have no effect.

## Error Handling

### gRPC errors

gRPC connection errors in streaming and batch consumers trigger automatic reconnection with exponential backoff. Errors are logged at warning or error level via `ILogger<Consumer>`.

### Handler exceptions

In streaming mode, exceptions thrown by the handler are logged and the message is automatically nacked with the exception message. The stream continues processing subsequent messages.

In batch mode, exceptions are your responsibility to handle. Ack or nack each message explicitly.

### Token refresh failures

Token refresh failures are logged and retried with exponential backoff. If refresh fails repeatedly, the client continues operating with the last valid token until the connection fails.

## Thread Safety

- `QueueTiClient` is safe to share across threads. `TokenStore` uses `ReaderWriterLockSlim` internally and disposal is guarded by an atomic flag.
- `Producer` and `Consumer` are stateless wrappers; they are safe to use concurrently from multiple tasks.
- `SetToken()` is thread-safe and updates the interceptor state immediately.
- `Dispose()` and `DisposeAsync()` are thread-safe and idempotent.

## Logging

The client uses `ILogger<QueueTiClient>` and `ILogger<Consumer>` for diagnostics. When using ASP.NET Core DI, configure logging as usual:

```csharp
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
```

Key events logged:
- Bearer token refresh success / failure.
- Consumer stream reconnection and backoff intervals.
- Handler exceptions (error level).
- Nack failures (error level).
