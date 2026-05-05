using QueueTi;
using System.Net;
using System.Net.Http.Json;
using System.Text;

internal sealed class MessageConsumerService : BackgroundService
{
    private static readonly TimeSpan _maxRegistrationBackoff = TimeSpan.FromSeconds(30);

    private readonly QueueTiClient _client;
    private readonly HttpClient _http;
    private readonly ILogger<MessageConsumerService> _logger;
    private readonly IConfiguration _configuration;

    public MessageConsumerService(
        QueueTiClient client,
        IHttpClientFactory httpClientFactory,
        ILogger<MessageConsumerService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _http = httpClientFactory.CreateClient();
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = _configuration.GetValue<string>("Consumer:Topic") ?? "messages";
        var group = _configuration.GetValue<string>("Consumer:Group") ?? "sample-consumer";
        var apiBaseUrl = _configuration.GetValue<string>("QueueTi:queue:HttpUrl");

        _logger.LogInformation("Starting consumer on topic '{Topic}', group '{Group}'", topic, group);

        if (apiBaseUrl is not null)
        {
            await RegisterConsumerGroupAsync(apiBaseUrl, topic, group, stoppingToken);
        }

        var consumer = _client.NewConsumer(topic, new ConsumerOptions
        {
            ConsumerGroup = group
        });

        await consumer.ConsumeAsync((msg, ct) =>
        {
            var text = Encoding.UTF8.GetString(msg.Payload);
            _logger.LogInformation(
                "Message {Id} (retry {Retry}): {Payload}",
                msg.Id, msg.RetryCount, text);
            return Task.CompletedTask;
        }, stoppingToken);
    }

    private async Task RegisterConsumerGroupAsync(string apiBaseUrl, string topic, string group, CancellationToken ct)
    {
        var url = $"{apiBaseUrl.TrimEnd('/')}/api/topics/{Uri.EscapeDataString(topic)}/consumer-groups";
        var backoff = TimeSpan.FromMilliseconds(500);

        while (true)
        {
            try
            {
                using var content = JsonContent.Create(new { consumer_group = group });
                var response = await _http.PostAsync(url, content, ct);
                if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict)
                {
                    _logger.LogInformation("Consumer group '{Group}' registered on topic '{Topic}'", group, topic);
                    return;
                }
                _logger.LogWarning("Consumer group registration returned {Status}; retrying in {Backoff}", (int)response.StatusCode, backoff);
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
