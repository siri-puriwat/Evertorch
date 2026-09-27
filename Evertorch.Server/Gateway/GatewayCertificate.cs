using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Evertorch.Server
{
/// <summary>
///     Finds the gateway's certificate (System Architecture §12): the configured PKCS#12 file, else the current user's
///     ASP.NET Core development certificate. Without either the start fails, naming the command that makes one.
/// </summary>
public static class GatewayCertificate
{
    public const string TrustCommand = "dotnet dev-certs https --trust";

    // The extension that marks the certificate `dotnet dev-certs` makes.
    private const string DevelopmentCertificateOid = "1.3.6.1.4.1.311.84.1.1";

    public static X509Certificate2 Load(GatewayOptions options, DateTime now)
    {
        if (!string.IsNullOrEmpty(options.CertificatePath))
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath, options.CertificatePassword);
            }
            catch (CryptographicException exception)
            {
                // The loader's own message never holds the password; this one names only the setting.
                throw new InvalidOperationException(
                    $"{GatewayOptions.SectionName}:CertificatePath could not be loaded as a PKCS#12 file with its "
                    + "password.",
                    exception);
            }
        }

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2? development = store.Certificates
            .Where(certificate => certificate.Extensions[DevelopmentCertificateOid] != null
                && certificate.HasPrivateKey
                && certificate.NotBefore <= now
                && certificate.NotAfter > now)
            .OrderByDescending(certificate => certificate.NotAfter)
            .FirstOrDefault();
        return development
            ?? throw new InvalidOperationException(
                $"The gateway has no certificate: set {GatewayOptions.SectionName}:CertificatePath, or make and trust "
                + $"the development certificate with `{TrustCommand}`.");
    }
}
}
