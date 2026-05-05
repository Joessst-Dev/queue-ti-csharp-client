using QueueTi;
using System.Text;

internal sealed class MessageConsumerService : BackgroundService
{
    private readonly QueueTiClient _client;
    private readonly ILogger<MessageConsumerService> _logger;
    private readonly IConfiguration _configuration;

    public MessageConsumerService(
        QueueTiClient client,
        ILogger<MessageConsumerService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = _configuration.GetValue<string>("Consumer:Topic") ?? "messages";
        var group = _configuration.GetValue<string>("Consumer:Group") ?? "sample-consumer";

        _logger.LogInformation("Starting consumer on topic '{Topic}', group '{Group}'", topic, group);

        var consumer = _client.NewConsumer(topic, new ConsumerOptions
        {
            ConsumerGroup = group
        });

        await consumer.ConsumeAsync(async (msg, ct) =>
        {
            var text = Encoding.UTF8.GetString(msg.Payload);
            _logger.LogInformation(
                "Message {Id} (retry {Retry}): {Payload}",
                msg.Id, msg.RetryCount, text);
            await Task.CompletedTask;
        }, stoppingToken);
    }
}
