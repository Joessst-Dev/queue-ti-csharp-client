using Aspire.Hosting.QueueTi;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("queueti-db");

var redis = builder.AddRedis("redis");

var queue = builder.AddQueueTi("queue")
    .WithNpgsqlDatabase(postgres)
    .WithRedis(redis);

builder.AddProject<Projects.QueueTi_Samples_Producer>("producer")
    .WithReference(queue)
    .WaitFor(queue);

builder.AddProject<Projects.QueueTi_Samples_Consumer>("consumer")
    .WithReference(queue)
    .WaitFor(queue);

builder.Build().Run();
