using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Agent;

public static class AgentIdentity
{
    public static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Midview", "agent-identity.pfx");

    public static X509Certificate2 LoadOrCreate(string certificatePath)
    {
        if (File.Exists(certificatePath))
        {
            return new X509Certificate2(certificatePath);
        }

        var certificate = SelfSignedCertificate.Create("MidviewAgent");
        var directory = Path.GetDirectoryName(certificatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        return certificate;
    }
}
