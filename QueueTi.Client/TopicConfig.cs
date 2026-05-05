namespace QueueTi;

public record TopicConfig(
    string Topic,
    bool Replayable,
    int? MaxRetries = null,
    int? MessageTtlSeconds = null,
    int? MaxDepth = null,
    int? ReplayWindowSeconds = null,
    int? ThroughputLimit = null);
