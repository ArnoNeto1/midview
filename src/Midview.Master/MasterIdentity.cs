using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// This file is a shared, portable credential by design (a classroom-wide
// authentication key), not a per-install identity: copying
// master-identity.pfx to another teacher's PC gives that PC the exact
// same certificate (same thumbprint), so it's already trusted on every
// Agent/Session that has this one thumbprint registered -- no lab
// machine needs to be touched again to add a second authorized teacher.
public static class MasterIdentity
{
    public static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Midview", "master-identity.pfx");

    public static X509Certificate2 LoadOrCreate(string certificatePath)
    {
        if (File.Exists(certificatePath))
        {
            return new X509Certificate2(certificatePath, (string?)null, X509KeyStorageFlags.Exportable);
        }

        var certificate = SelfSignedCertificate.Create("MidviewMaster");
        var directory = Path.GetDirectoryName(certificatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        return certificate;
    }
}
