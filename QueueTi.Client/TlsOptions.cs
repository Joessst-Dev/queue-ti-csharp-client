namespace QueueTi;

/// <summary>
/// TLS configuration for <see cref="QueueTiClient"/> and <see cref="AdminClient"/>.
/// </summary>
/// <remarks>
/// All certificate and key inputs are PEM-encoded byte arrays so that callers may load
/// them from any source (file, secret store, embedded resource) without forcing a
/// particular file-system layout. When <see cref="RootCertificates"/> is null, the
/// system trust store is used for server certificate verification.
/// </remarks>
public sealed class TlsOptions
{
    /// <summary>PEM-encoded CA certificate(s). Null uses system CAs.</summary>
    public byte[]? RootCertificates { get; init; }

    /// <summary>PEM-encoded client private key for mTLS. Requires <see cref="CertificateChain"/>.</summary>
    public byte[]? PrivateKey { get; init; }

    /// <summary>PEM-encoded client certificate chain for mTLS. Requires <see cref="PrivateKey"/>.</summary>
    public byte[]? CertificateChain { get; init; }

    /// <summary>Override the TLS SNI hostname used for certificate verification.</summary>
    public string? ServerNameOverride { get; init; }
}
