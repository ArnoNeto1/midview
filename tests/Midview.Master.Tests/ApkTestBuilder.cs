using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Builds tiny synthetic APKs (prefix + APK Signing Block + empty Central
// Directory + End of Central Directory) so signature reading is tested without
// a real Android build.
internal static class ApkTestBuilder
{
    public const int V2Id = 0x7109871a;
    public const int V3Id = unchecked((int)0xf05368c0);

    public static byte[] NewCertificate(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return cert.Export(X509ContentType.Cert);
    }

    public static string Checksum(byte[] cert) =>
        Convert.ToBase64String(SHA256.HashData(cert)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // A v2/v3 value holding one signer with one certificate.
    public static byte[] SignerBlock(byte[] cert)
    {
        var certs = Prefixed(Prefixed(cert));
        var signedData = Prefixed(Concat(Prefixed(Array.Empty<byte>()), certs));
        var signer = Concat(signedData, Prefixed(Array.Empty<byte>()), Prefixed(Array.Empty<byte>()));
        return Prefixed(Prefixed(signer));
    }

    public static byte[] Apk(params (int Id, byte[] Value)[] pairs)
    {
        var body = new MemoryStream();
        foreach (var (id, value) in pairs)
        {
            body.Write(BitConverter.GetBytes((long)(value.Length + 4)));
            body.Write(BitConverter.GetBytes(id));
            body.Write(value);
        }
        var size = body.Length + 24;
        var apk = new MemoryStream();
        apk.Write(new byte[100]); // stands for the zip entries
        apk.Write(BitConverter.GetBytes(size));
        apk.Write(body.ToArray());
        apk.Write(BitConverter.GetBytes(size));
        apk.Write(System.Text.Encoding.ASCII.GetBytes("APK Sig Block 42"));
        return WithEocd(apk, (int)apk.Length);
    }

    public static byte[] ZipWithoutBlock()
    {
        var apk = new MemoryStream();
        apk.Write(new byte[100]);
        return WithEocd(apk, 100);
    }

    private static byte[] WithEocd(MemoryStream apk, int cdOffset)
    {
        apk.Write(BitConverter.GetBytes(0x06054b50));
        apk.Write(new byte[4]);
        apk.Write(new byte[4]); // entry counts
        apk.Write(BitConverter.GetBytes(0)); // CD size
        apk.Write(BitConverter.GetBytes(cdOffset));
        apk.Write(new byte[2]); // comment length
        return apk.ToArray();
    }

    private static byte[] Prefixed(byte[] data) => Concat(BitConverter.GetBytes(data.Length), data);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
