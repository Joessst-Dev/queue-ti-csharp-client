namespace QueueTi;

public sealed record ConsumerOptions
{
    public int Concurrency { get; init; } = 1;
    public uint? VisibilityTimeoutSeconds { get; init; }
    public string ConsumerGroup { get; init; } = "";
}
