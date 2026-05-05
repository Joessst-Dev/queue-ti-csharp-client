namespace QueueTi;

public sealed class QueueTiConflictException : Exception
{
    public QueueTiConflictException()
        : base("A conflict occurred with the current state of the resource.") { }

    public QueueTiConflictException(string message)
        : base(message) { }

    public QueueTiConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
