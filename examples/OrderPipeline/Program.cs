using System.Text;
using System.Text.Json;
using QueueTi;

const string GrpcAddress = "http://localhost:50051";
const string HttpAddress = "http://localhost:8080";
const string Topic = "orders";
const string DlqTopic = "orders.dlq";
const string ConsumerGroup = "order-processor";
const string DlqConsumerGroup = "order-processor-dlq";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

string? bearerToken = null;

if (await QueueTiAuth.GetAuthRequiredAsync(HttpAddress, insecure: true, cts.Token))
{
    var username = Environment.GetEnvironmentVariable("QUEUETI_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("QUEUETI_PASSWORD") ?? "admin";
    Console.WriteLine("[setup] Auth enabled — logging in...");
    bearerToken = await QueueTiAuth.LoginAsync(HttpAddress, username, password, insecure: true, cts.Token);
}

var clientOptions = new QueueTiClientOptions { Insecure = true, BearerToken = bearerToken };

await using var client = QueueTiClient.Create(GrpcAddress, clientOptions);
await using var admin = AdminClient.Create(HttpAddress, clientOptions);

// Configure the topic so nacked messages flow to DLQ after 2 retries
Console.WriteLine($"[setup] Configuring topic '{Topic}' (MaxRetries=2)...");
await admin.UpsertTopicConfigAsync(Topic, new TopicConfig(Topic, Replayable: false, MaxRetries: 2), cts.Token);

// Register the main consumer group; DLQ group is registered after consuming
// because the DLQ topic may not exist until the first message is dead-lettered.
await RegisterGroupAsync(admin, Topic, ConsumerGroup, cts.Token);

// Publish 5 orders — order #3 is a poison pill
Console.WriteLine("\n[producer] Publishing 5 orders...");
var producer = client.NewProducer();
await PublishOrdersAsync(producer, Topic, cts.Token);

// Stream-consume with concurrency=3; handler throws on poison → auto-nack
Console.WriteLine("\n[consumer] Consuming orders (Ctrl+C to stop)...");
var consumer = client.NewConsumer(Topic, new ConsumerOptions
{
    ConsumerGroup = ConsumerGroup,
    Concurrency = 3,
});
await consumer.ConsumeAsync(HandleOrderAsync, cts.Token);

// Register the DLQ consumer group now that the topic exists, then drain
Console.WriteLine("\n[dlq] Registering DLQ consumer group and draining...");
await RegisterGroupAsync(admin, DlqTopic, DlqConsumerGroup, CancellationToken.None);
var dlqConsumer = client.NewConsumer(DlqTopic, new ConsumerOptions { ConsumerGroup = DlqConsumerGroup });
await DrainDlqAsync(dlqConsumer);

Console.WriteLine("\nDone.");

// ────────────────────────────────────────────────────────────────

static async Task RegisterGroupAsync(AdminClient admin, string topic, string group, CancellationToken ct)
{
    try
    {
        await admin.RegisterConsumerGroupAsync(topic, group, ct);
        Console.WriteLine($"         Registered '{group}' on '{topic}'");
    }
    catch (QueueTiConflictException)
    {
        Console.WriteLine($"         '{group}' already registered on '{topic}'");
    }
}

static async Task PublishOrdersAsync(Producer producer, string topic, CancellationToken ct)
{
    var orders = new[]
    {
        (orderId: "ord-001", customerId: "cust-A", amount: 49.99m,  poison: false),
        (orderId: "ord-002", customerId: "cust-B", amount: 129.00m, poison: false),
        (orderId: "ord-003", customerId: "cust-C", amount: 0.01m,   poison: true),
        (orderId: "ord-004", customerId: "cust-D", amount: 75.50m,  poison: false),
        (orderId: "ord-005", customerId: "cust-E", amount: 200.00m, poison: false),
    };

    foreach (var (orderId, customerId, amount, poison) in orders)
    {
        var payload = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { orderId, customerId, amount }));

        var metadata = new Dictionary<string, string>
        {
            ["order-id"] = orderId,
            ["customer-id"] = customerId,
        };

        if (poison)
        {
            metadata["type"] = "poison";
        }

        var id = await producer.PublishAsync(topic, payload, new PublishOptions { Metadata = metadata }, ct);
        Console.WriteLine($"         {orderId}{(poison ? " [POISON]" : "")} → {id}");
    }
}

static async Task HandleOrderAsync(QueueTiMessage msg, CancellationToken ct)
{
    var orderId = msg.Metadata.GetValueOrDefault("order-id", msg.Id);

    if (msg.Metadata.TryGetValue("type", out var type) && type == "poison")
    {
        Console.WriteLine($"[consumer] NACK {orderId} — poison pill (retry {msg.RetryCount})");
        throw new InvalidOperationException($"Poison pill: {orderId}");
    }

    var body = Encoding.UTF8.GetString(msg.Payload);
    Console.WriteLine($"[consumer] ACK  {orderId}: {body}");
    await Task.Delay(100, ct);
}

static async Task DrainDlqAsync(Consumer dlqConsumer)
{
    // Start with a 10 s window; tighten to 2 s after the first batch lands so
    // we don't time out before the server delivers dead-lettered messages.
    using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var count = 0;

    await dlqConsumer.ConsumeBatchAsync(10, async (batch, ct) =>
    {
        foreach (var msg in batch)
        {
            var orderId = msg.Metadata.GetValueOrDefault("order-id", msg.Id);
            Console.WriteLine($"[dlq]      ACK  {orderId} (retries: {msg.RetryCount})");
            await msg.AckAsync(ct);
            Interlocked.Increment(ref count);
        }
        drainCts.CancelAfter(TimeSpan.FromSeconds(2));
    }, drainCts.Token);

    Console.WriteLine($"[dlq] Drain complete — {count} dead-lettered message(s) acked.");
}
