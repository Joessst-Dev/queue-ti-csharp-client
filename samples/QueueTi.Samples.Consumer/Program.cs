using Microsoft.Extensions.Hosting;
using QueueTi.Aspire;

HostApplicationBuilder builder = new(args);

builder.AddQueueTiClient("queue");
builder.Services.AddHostedService<MessageConsumerService>();

builder.Build().Run();
