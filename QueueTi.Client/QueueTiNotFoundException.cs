namespace QueueTi;

public sealed class QueueTiNotFoundException : Exception
{
    public QueueTiNotFoundException()
        : base("The requested resource was not found.") { }

    public QueueTiNotFoundException(string message)
        : base(message) { }

    public QueueTiNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
