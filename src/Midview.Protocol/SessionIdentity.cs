using System.Security.Cryptography.X509Certificates;

namespace Midview.Protocol;

// Persisted, so the Session thumbprint a teacher saves in MidviewMaster's
// classroom list stays valid across reboots (a fresh per-launch certificate
// would silently break every saved entry the next time the machine starts).
//
// Lives in the Agent's data directory. The Agent (running as SYSTEM)
// creates it before it first launches Session, because Session runs as the
// logged-in student, who can read that directory but not write to it. Both
// of them load it, which is why it's here in Midview.Protocol.
public static class SessionIdentity
{
    public static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Midview", "session-identity.pfx");

    public static X509Certificate2 LoadOrCreate(string certificatePath)
    {
        if (File.Exists(certificatePath))
        {
            return new X509Certificate2(certificatePath, (string?)null, X509KeyStorageFlags.Exportable);
        }

        var certificate = SelfSignedCertificate.Create(Environment.MachineName);
        var directory = Path.GetDirectoryName(certificatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        return certificate;
    }
}
