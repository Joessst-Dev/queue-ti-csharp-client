namespace QueueTi;

public sealed class QueueTiAuthSession
{
    internal static readonly QueueTiAuthSession NoOp = new()
    {
        Token = null,
        RefreshAsync = static _ => Task.FromResult(string.Empty),
    };

    public string? Token { get; init; }
    public required Func<CancellationToken, Task<string>> RefreshAsync { get; init; }
}
