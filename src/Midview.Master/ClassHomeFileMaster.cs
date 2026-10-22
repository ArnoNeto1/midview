using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Midview.Protocol;

namespace Midview.Master;

// Another teacher PC this one saw as the class folders' home (Ruling 11): its
// certificate thumbprint, computer name and generation (ClassHome).
public sealed record SeenHome(string Thumbprint, string Name, long Generation)
{
    public static SeenHome Of(ClassHome home) => new(home.Thumbprint, home.Name, home.Generation);

    // What an Agent keeping it would answer this PC's claim.
    [JsonIgnore]
    public string RefusalText =>ClassHomeClaim.OtherHomeText + (Name.Length > 0 ? Name : ClassHome.UnnamedText);

    internal bool IsWellFormed =>
        TeacherInvite.IsThumbprint(Thumbprint) && Name is { Length: <= ClassHome.MaxNameLength } && Generation >= 0;
}

// What this teacher PC knows of the class folders' home (plan Ruling 9 and
// 11), kept next to the classroom list (MasterData.Folder): its own
// generation as the home (ClassHome.Generation), when it became the home in
// UTC ticks on its own clock, which its claims carry ever after; and the last
// other PC it saw as the home, so that a PC that was never the home but saw
// another one never becomes the home by starting a lesson while only Agents
// with no home are on (only by taking the folders over). Written whole through
// a temp file, so a crash never leaves half of it; a file that is missing,
// can't be read or isn't one means neither (this PC was never the home and
// saw none). path: elsewhere than Path (a test's folder).
public static class ClassHomeFileMaster
{
    // The tracker and the sync write it from their own threads.
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(MasterData.Folder, "class-home.json");

    // This PC's generation; null when it was never the home.
    public static long? Read(string? path = null) => Load(path ?? Path).Generation is > 0 and var generation ? generation : null;

    // The last other PC this one saw as the home; null for none.
    public static SeenHome? ReadLastOtherHome(string? path = null) =>
        Load(path ?? Path).LastOtherHome is { IsWellFormed: true } seen ? seen : null;

    // Keeps the last other home seen. Throws if it can't be written.
    public static void Write(long generation, string? path = null)
    {
        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        path ??= Path;
        lock (Gate)
        {
            Save(path, Load(path) with { Generation = generation });
        }
    }

    // Keeps this PC's generation; writes nothing when that home is already
    // the one on file. Throws if it can't be written.
    public static void WriteLastOtherHome(SeenHome home, string? path = null)
    {
        if (!home.IsWellFormed)
        {
            throw new ArgumentException("Not a home.", nameof(home));
        }

        path ??= Path;
        lock (Gate)
        {
            var stored = Load(path);
            if (stored.LastOtherHome != home)
            {
                Save(path, stored with { LastOtherHome = home });
            }
        }
    }

    // The generation a takeover claims with: newer than every home the Agents
    // report, and than now (UTC ticks) when this PC's clock is ahead of theirs.
    public static long TakeOverGeneration(long nowUtcTicks, IEnumerable<ClassHome> reported)
    {
        var newest = reported.Select(home => home.Generation).DefaultIfEmpty(0).Max();
        return Math.Max(nowUtcTicks, newest == long.MaxValue ? long.MaxValue : newest + 1);
    }

    private static Stored Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { } stored)
            {
                return stored;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Neither, as for no file.
        }
        return new Stored(null, null);
    }

    private static void Save(string path, Stored stored)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, stored);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record Stored(long? Generation, SeenHome? LastOtherHome);
}
