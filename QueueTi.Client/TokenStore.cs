using System.Text;
using System.Text.Json;

namespace QueueTi;

public sealed class TokenStore : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private string _token;

    public TokenStore(string initialToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialToken);
        _token = initialToken;
    }

    public string Get()
    {
        _lock.EnterReadLock();
        try
        {
            return _token;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Set(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _lock.EnterWriteLock();
        try
        {
            _token = token;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public DateTimeOffset GetExpiry()
    {
        var token = Get();
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("JWT must have exactly three dot-separated segments.");
        }

        var payload = parts[1];

        // Base64url to standard base64
        var padded = payload.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        padded = remainder switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException ex)
        {
            throw new FormatException("JWT payload segment is not valid base64url.", ex);
        }

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        if (!doc.RootElement.TryGetProperty("exp", out var expElement))
        {
            throw new FormatException("JWT payload does not contain an 'exp' claim.");
        }

        if (!expElement.TryGetInt64(out var expSeconds))
        {
            throw new FormatException("JWT 'exp' claim is not a valid integer.");
        }

        return DateTimeOffset.FromUnixTimeSeconds(expSeconds);
    }

    public void Dispose() => _lock.Dispose();
}
