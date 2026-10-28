using System.IO;
using System.Security.Cryptography;

namespace Midview.Master;

// The lab's administrator password, kept so the teacher can have it typed into
// a student's UAC prompt with one click. On disk it exists only as a DPAPI
// CurrentUser blob: another Windows user (or another PC) can't open it. The
// caller clears the buffers it gets from Load and hands to Save.
public sealed class AdminPasswordStore
{
    private readonly string _path;

    public AdminPasswordStore(string folder) => _path = Path.Combine(folder, "admin-password.bin");

    public bool HasPassword => File.Exists(_path);

    public void Save(byte[] passwordUtf8)
    {
        var blob = ProtectedData.Protect(passwordUtf8, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, blob);
    }

    // Null when there is no password or it can't be opened (damaged file, or
    // saved by another Windows user); never throws for those.
    public byte[]? Load()
    {
        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(_path), optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
