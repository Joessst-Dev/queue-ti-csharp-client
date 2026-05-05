using Microsoft.Extensions.Hosting;
using QueueTi.Aspire;

HostApplicationBuilder builder = new(args);

builder.AddQueueTiClient("queue");
builder.Services.AddHttpClient();
builder.Services.AddHostedService<MessageConsumerService>();

builder.Build().Run();
