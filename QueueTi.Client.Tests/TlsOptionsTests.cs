namespace QueueTi.Client.Tests;

public sealed class TlsOptionsTests
{
    // ── QueueTiClient.Create — mutual exclusion ─────────────────────────────────

    [Fact]
    public void QueueTiClient_Create_GivenInsecureAndTls_ShouldThrow()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            Insecure = true,
            Tls = new TlsOptions(),
        };

        // Act (When)
        var ex = Record.Exception(() => QueueTiClient.Create("http://localhost", options));

        // Assert (Then)
        Assert.IsType<ArgumentException>(ex);
        Assert.Contains("mutually exclusive", ex!.Message);
    }

    // ── AdminClient.Create — mutual exclusion ───────────────────────────────────

    [Fact]
    public void AdminClient_Create_GivenInsecureAndTls_ShouldThrow()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            Insecure = true,
            Tls = new TlsOptions(),
        };

        // Act (When)
        var ex = Record.Exception(() => AdminClient.Create("http://localhost", options));

        // Assert (Then)
        Assert.IsType<ArgumentException>(ex);
        Assert.Contains("mutually exclusive", ex!.Message);
    }

    // ── TlsOptions — mTLS pair validation ───────────────────────────────────────

    [Fact]
    public void TlsOptions_GivenOnlyPrivateKeyWithoutCertChain_ShouldThrowOnCreate()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            Tls = new TlsOptions
            {
                PrivateKey = new byte[] { 1 },
                CertificateChain = null,
            },
        };

        // Act (When)
        var ex = Record.Exception(() => QueueTiClient.Create("https://localhost", options));

        // Assert (Then)
        Assert.IsType<ArgumentException>(ex);
        Assert.Contains("PrivateKey and CertificateChain", ex!.Message);
    }

    [Fact]
    public void TlsOptions_GivenOnlyCertChainWithoutPrivateKey_ShouldThrowOnCreate()
    {
        // Arrange (Given)
        var options = new QueueTiClientOptions
        {
            Tls = new TlsOptions
            {
                PrivateKey = null,
                CertificateChain = new byte[] { 1 },
            },
        };

        // Act (When)
        var ex = Record.Exception(() => QueueTiClient.Create("https://localhost", options));

        // Assert (Then)
        Assert.IsType<ArgumentException>(ex);
        Assert.Contains("PrivateKey and CertificateChain", ex!.Message);
    }
}
