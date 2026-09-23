using System.Text.Json;

namespace Midview.Agent;

public sealed class SiteBlockState
{
    public bool InternetBlocked { get; set; }
    public HashSet<string> BlockedDomains { get; set; } = new();

    public static SiteBlockState Load(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return new SiteBlockState();
        }

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<SiteBlockState>(json) ?? new SiteBlockState();
        }
        catch (JsonException)
        {
            // An unreadable file (a power cut mid-write by an older version,
            // a bad hand edit) used to stop the Agent from listening at all,
            // leaving a blocked machine blocked with no way to unblock it
            // remotely. Starting with nothing blocked is the safe direction:
            // the teacher can always block again. The file is kept aside, not
            // deleted, so it can still be looked at.
            File.Move(statePath, statePath + ".corrupt", overwrite: true);
            return new SiteBlockState();
        }
    }

    public void Save(string statePath)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written in full to a temp file, flushed to disk, then swapped in: a
        // power cut at any point leaves either the old or the new state, never
        // a truncated file.
        var tempPath = statePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, this);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, statePath, overwrite: true);
    }
}
