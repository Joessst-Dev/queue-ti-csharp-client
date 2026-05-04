namespace QueueTi;

public sealed record PublishOptions
{
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public string? Key { get; init; }
}
