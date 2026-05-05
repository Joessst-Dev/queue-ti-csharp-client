using System.Text;
using QueueTi;

internal sealed class MessageConsumerService : BackgroundService
{
    private static readonly TimeSpan _maxRegistrationBackoff = TimeSpan.FromSeconds(30);

    private readonly QueueTiClient _client;
    private readonly AdminClient _admin;
    private readonly ILogger<MessageConsumerService> _logger;
    private readonly IConfiguration _configuration;

    public MessageConsumerService(
        QueueTiClient client,
        AdminClient admin,
        ILogger<MessageConsumerService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _admin = admin;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = _configuration.GetValue<string>("Consumer:Topic") ?? "messages";
        var group = _configuration.GetValue<string>("Consumer:Group") ?? "sample-consumer";

        _logger.LogInformation("Starting consumer on topic '{Topic}', group '{Group}'", topic, group);

        await RegisterConsumerGroupAsync(topic, group, stoppingToken);

        var consumer = _client.NewConsumer(topic, new ConsumerOptions { ConsumerGroup = group });

        await consumer.ConsumeAsync((msg, ct) =>
        {
            var text = Encoding.UTF8.GetString(msg.Payload);
            _logger.LogInformation("Message {Id} (retry {Retry}): {Payload}", msg.Id, msg.RetryCount, text);
            return Task.CompletedTask;
        }, stoppingToken);
    }

    private async Task RegisterConsumerGroupAsync(string topic, string group, CancellationToken ct)
    {
        var backoff = TimeSpan.FromMilliseconds(500);

        while (true)
        {
            try
            {
                await _admin.RegisterConsumerGroupAsync(topic, group, ct);
                _logger.LogInformation("Consumer group '{Group}' registered on topic '{Topic}'", group, topic);
                return;
            }
            catch (QueueTiConflictException)
            {
                _logger.LogInformation("Consumer group '{Group}' already registered on topic '{Topic}'", group, topic);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Consumer group registration failed; retrying in {Backoff}", backoff);
            }

            await Task.Delay(backoff, ct);
            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, _maxRegistrationBackoff.TotalMilliseconds));
        }
    }
}
