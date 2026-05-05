using QueueTi;
using QueueTi.Aspire;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddQueueTiClient("queue");

var app = builder.Build();

app.MapPost("/messages/{topic}", async (string topic, PublishRequest request, QueueTiClient client) =>
{
    var producer = client.NewProducer();
    var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
    var id = await producer.PublishAsync(topic, payload);
    return Results.Ok(new { id });
});

app.MapHealthChecks("/health");

app.Run();

record PublishRequest(string Message, Dictionary<string, string>? Metadata = null);
