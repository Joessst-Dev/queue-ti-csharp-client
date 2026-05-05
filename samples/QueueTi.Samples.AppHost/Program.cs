using Aspire.Hosting.QueueTi;

var builder = DistributedApplication.CreateBuilder(args);

// Special characters in Aspire-generated passwords break the postgres:// URL that QueueTi
// constructs internally from individual env vars. Restrict to alphanumeric to stay URL-safe.
var pgPassword = builder.AddParameter("pg-password",
    new GenerateParameterDefault { MinLength = 22, Special = false },
    secret: true);

var postgres = builder.AddPostgres("postgres", password: pgPassword)
    .AddDatabase("queueti-db");

var redis = builder.AddRedis("redis");

var queue = builder.AddQueueTi("queue")
    .WithNpgsqlDatabase(postgres)
    .WithRedis(redis);

const string SampleTopic = "messages";

builder.AddProject<Projects.QueueTi_Samples_Producer>("producer")
    .WithReference(queue)
    .WaitFor(queue)
    .WithEnvironment("QueueTi__queue__HttpUrl", queue.GetEndpoint("http"));

builder.AddProject<Projects.QueueTi_Samples_Consumer>("consumer")
    .WithReference(queue)
    .WaitFor(queue)
    .WithEnvironment("QueueTi__queue__HttpUrl", queue.GetEndpoint("http"))
    .WithEnvironment("Consumer__Topic", SampleTopic)
    .WithEnvironment("Consumer__Group", "sample-consumer");

builder.Build().Run();
