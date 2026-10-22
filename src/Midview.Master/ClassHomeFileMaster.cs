using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// This teacher PC's generation as the class folders' home (ClassHome.
// Generation, plan Ruling 9): when it became the home, in UTC ticks on its
// own clock, which its claims carry ever after. Kept next to the classroom
// list (MasterData.Folder), written through a temp file so a crash never
// leaves half of it; a file that is missing, can't be read or isn't one means
// none (this PC was never the home). path: elsewhere than Path (a test's
// folder).
public static class ClassHomeFileMaster
{
    public static string Path => System.IO.Path.Combine(MasterData.Folder, "class-home.json");

    public static long? Read(string? path = null)
    {
        path ??= Path;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            return stored is { Generation: > 0 } ? stored.Generation : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    // Throws if it can't be written.
    public static void Write(long generation, string? path = null)
    {
        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        path ??= Path;
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Stored(generation));
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    // The generation a takeover claims with: newer than every home the Agents
    // report, and than now (UTC ticks) when this PC's clock is ahead of theirs.
    public static long TakeOverGeneration(long nowUtcTicks, IEnumerable<ClassHome> reported)
    {
        var newest = reported.Select(home => home.Generation).DefaultIfEmpty(0).Max();
        return Math.Max(nowUtcTicks, newest == long.MaxValue ? long.MaxValue : newest + 1);
    }

    private sealed record Stored(long Generation);
}
