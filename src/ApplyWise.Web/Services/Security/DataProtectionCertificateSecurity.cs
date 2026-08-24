using System.Security.Cryptography.X509Certificates;

namespace ApplyWise.Web.Services.Security;

public static class DataProtectionCertificateSecurity
{
    public static void EnsureProductionReady(
        X509Certificate2 certificate,
        DateTimeOffset currentTime)
    {
        EnsureCanDecrypt(certificate);

        var now = currentTime.UtcDateTime;
        if (now < certificate.NotBefore.ToUniversalTime()
            || now > certificate.NotAfter.ToUniversalTime())
        {
            throw new InvalidOperationException(
                "The Data Protection certificate is not currently valid.");
        }
    }

    public static void EnsureCanDecrypt(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "The Data Protection certificate must contain its private key.");
        }

        using var privateKey = certificate.GetRSAPrivateKey();
        if (privateKey is null || privateKey.KeySize < 2048)
        {
            throw new InvalidOperationException(
                "The Data Protection certificate must contain an RSA private key of at least 2048 bits.");
        }
    }
}
