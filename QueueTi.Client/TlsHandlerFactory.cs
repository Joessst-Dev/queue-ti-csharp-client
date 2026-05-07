using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace QueueTi;

internal static class TlsHandlerFactory
{
    internal static HttpMessageHandler Build(TlsOptions tls)
    {
        if ((tls.PrivateKey is null) != (tls.CertificateChain is null))
        {
            throw new ArgumentException(
                "TlsOptions: PrivateKey and CertificateChain must both be set for mTLS, or neither.");
        }

        var ownedCerts = new List<X509Certificate2>();
        var inner = new SocketsHttpHandler();
        try
        {
            var sslOptions = new SslClientAuthenticationOptions();

            if (tls.RootCertificates is not null)
            {
                var caCert = X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(tls.RootCertificates));
                ownedCerts.Add(caCert);

                sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, _) =>
                {
                    if (cert is not X509Certificate2 serverCert || chain is null)
                    {
                        return false;
                    }
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    return chain.Build(serverCert);
                };
            }

            if (tls.PrivateKey is not null && tls.CertificateChain is not null)
            {
                var clientCert = X509Certificate2.CreateFromPem(
                    Encoding.UTF8.GetString(tls.CertificateChain),
                    Encoding.UTF8.GetString(tls.PrivateKey));
                ownedCerts.Add(clientCert);
                sslOptions.ClientCertificates = new X509CertificateCollection { clientCert };
            }

            if (tls.ServerNameOverride is not null)
            {
                sslOptions.TargetHost = tls.ServerNameOverride;
            }

            inner.SslOptions = sslOptions;
            return new OwnedCertsHandler(inner, ownedCerts);
        }
        catch
        {
            foreach (var cert in ownedCerts) cert.Dispose();
            inner.Dispose();
            throw;
        }
    }

    private sealed class OwnedCertsHandler(SocketsHttpHandler inner, List<X509Certificate2> ownedCerts)
        : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (var cert in ownedCerts)
                {
                    cert.Dispose();
                }
            }
        }
    }
}
