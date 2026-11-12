using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's current app block for the class's phones (see
// PhoneAppPolicy), kept apart from the PCs' program block (AppBlockFile):
// the phones' names are Android packages, not .exe files. It survives Master
// restarts so the pollers can resend it to phones that were off. A block sent
// to a few phones only is not kept here.
public static class PhoneAppBlockFile
{
    public static string DefaultPath => Path.Combine(MasterData.Folder, "phone-app-block.json");

    // Missing or unreadable means nothing blocked on the phones; an
    // unreadable file is moved aside rather than lost.
    public static PhoneAppPolicy? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredPolicy>(File.ReadAllText(path));
            var policy = stored is null ? null : new PhoneAppPolicy(stored.SavedAt, stored.Mode, stored.Packages ?? new List<string>());
            return policy is not null && policy.IsValid() ? policy : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Left where it is: read (and refused) again at the next start.
            }
            return null;
        }
    }

    public static void Save(string path, PhoneAppPolicy policy)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new StoredPolicy(policy.SavedAt, policy.Mode, policy.Packages.ToList())));
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record StoredPolicy(long SavedAt, AppBlockMode Mode, List<string>? Packages);
}
