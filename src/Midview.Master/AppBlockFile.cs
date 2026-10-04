using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's current class-wide program block (see AppBlockPolicy), next
// to the classroom list, so it survives Master restarts and the pollers can
// resend it to machines that were off. A block sent to one machine only is
// not kept here.
public static class AppBlockFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Midview", "app-block.json");

    // Missing or unreadable means nothing blocked class-wide; an unreadable
    // file is moved aside rather than lost.
    public static AppBlockPolicy? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredPolicy>(File.ReadAllText(path));
            var policy = stored is null ? null : new AppBlockPolicy(stored.SavedAt, stored.Mode, stored.Programs ?? new List<string>());
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

    public static void Save(string path, AppBlockPolicy policy)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new StoredPolicy(policy.SavedAt, policy.Mode, policy.Programs.ToList())));
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record StoredPolicy(long SavedAt, AppBlockMode Mode, List<string>? Programs);
}
