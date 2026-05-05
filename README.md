# QueueTi C# Client

A proto-first gRPC client library for the [QueueTi](https://github.com/Joessst-Dev/queue-ti) distributed message queue service. Targets `.NET 10.0`.

## Installation

> **Note:** The package has not yet been published to NuGet. To use it now, clone the repository and add a project reference, or build the package locally with `dotnet pack QueueTi.Client/`.

Once published, it will be installable via:

```bash
dotnet add package QueueTi.Client
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
