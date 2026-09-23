using System.Security.Cryptography.X509Certificates;

namespace Midview.Protocol;

// Created by the Agent (running as SYSTEM) rather than by Session itself:
// Session runs as the logged-in student, a standard user who can only read
// %ProgramData%\Midview, never write to it. A fresh certificate per launch
// (the old behavior) meant a new thumbprint after every reboot or restart,
// printed to a console nobody sees once the Agent auto-launches Session --
// leaving the teacher no way to connect at all.
//
// The private key is readable by the student on purpose: Session runs as
// that student, so anything Session can read, the student can too. All it
// lets them do is impersonate their own machine's screen to the teacher.
public static class SessionIdentity
{
    public static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Midview", "session-identity.pfx");

    public static string EnsureExists(string certificatePath)
    {
        if (File.Exists(certificatePath))
        {
            using var existing = new X509Certificate2(
                certificatePath, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
            return existing.Thumbprint;
        }

        using var certificate = SelfSignedCertificate.Create("MidviewSession");
        var directory = Path.GetDirectoryName(certificatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        return certificate.Thumbprint;
    }

    public static X509Certificate2 Load(string certificatePath) =>
        new(certificatePath, (string?)null, X509KeyStorageFlags.UserKeySet);
}
