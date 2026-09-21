using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Midview.Protocol;

public static class SelfSignedCertificate
{
    public static X509Certificate2 Create(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }
}
