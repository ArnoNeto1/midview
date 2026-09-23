using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The teacher's own classroom roster -- not a security boundary (a wrong
// entry just fails to connect), so a plain per-user file rather than the
// ACL-protected data directory trusted-masters.json lives in.
public static class KnownMachinesFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Midview", "known-machines.json");

    // An unreadable file is moved aside to "<path>.corrupt" (returned
    // through corruptCopyPath) rather than deleted, or left in place to
    // crash Master on every start: the roster is the teacher's own work, so
    // it has to stay recoverable by hand.
    public static List<KnownMachine> Load(string path, out string? corruptCopyPath)
    {
        corruptCopyPath = null;
        if (!File.Exists(path))
        {
            return new List<KnownMachine>();
        }

        try
        {
            var machines = JsonSerializer.Deserialize<List<KnownMachine?>>(File.ReadAllText(path)) ?? new();
            // A hand-edited entry missing a required field would only fail
            // later, deep inside the UI or a connection attempt.
            return machines
                .Where(m => m is { Name: not null, HostAddress: not null, SessionThumbprint: not null })
                .Select(m => m!)
                .ToList();
        }
        catch (JsonException)
        {
            corruptCopyPath = path + ".corrupt";
            File.Move(path, corruptCopyPath, overwrite: true);
            return new List<KnownMachine>();
        }
    }

    public static void Save(string path, IEnumerable<KnownMachine> machines)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Same temp-file-then-swap as the Agent's SiteBlockState: a crash or
        // power cut mid-save must never leave a truncated roster behind.
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, machines.ToList(), WriteOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}
