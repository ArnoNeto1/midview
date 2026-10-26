using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// What this teacher PC knows of the class folders' home, kept next to the
// classroom list (MasterData.Folder): its own
// generation as the home (ClassHome.Generation), when it became the home in
// UTC ticks on its own clock, which its claims carry ever after; the last
// other PC it saw as the home, so that a PC that was never the home but saw
// another one never becomes the home by starting a lesson while only Agents
// with no home are on (only by taking the folders over), and goes on working
// through that PC; and the PC it handed the folders over to. Written whole
// through a temp file, so a crash never leaves half of it. A file that isn't
// there, or isn't one, means none of these (this PC was never the home, saw
// none and handed nothing over); a file that is there but can't be read right
// now is tried once more after a moment, then reading it throws (IOException
// or UnauthorizedAccessException) rather than pass for none.
// path: elsewhere than Path (a test's folder).
public static class ClassHomeFileMaster
{
    // How long a file that couldn't be read gets before the second try.
    private static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(200);

    // The tracker and the sync write it from their own threads.
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(MasterData.Folder, "class-home.json");

    // This PC's generation; null when it was never the home.
    public static long? Read(string? path = null) => Load(path ?? Path).Generation is > 0 and var generation ? generation : null;

    // The last other PC this one saw as the home; null for none.
    public static ClassHome? ReadLastOtherHome(string? path = null) =>
        Load(path ?? Path).LastOtherHome is { } stored && stored.ToHome() is { } home && IsHome(home) ? home : null;

    // Keeps this PC's generation. Throws if it can't be read or written.
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

    // Keeps the last other home seen; writes nothing when that home is
    // already the one on file. The same PC (its certificate and name) never
    // goes back to an older generation (a tracker's refresh that read the
    // Agents before that PC took the room back, writing after): its
    // generation stays, the rest is taken. Throws if it can't be read or written.
    public static void WriteLastOtherHome(ClassHome home, string? path = null)
    {
        if (!IsHome(home))
        {
            throw new ArgumentException("Not a home.", nameof(home));
        }

        path ??= Path;
        lock (Gate)
        {
            var file = Load(path);
            var stored = StoredHome.Of(file.LastOtherHome is { } kept && IsSamePc(kept, home) && kept.Generation > home.Generation
                ? home with { Generation = kept.Generation }
                : home);
            if (file.LastOtherHome != stored)
            {
                Save(path, file with { LastOtherHome = stored });
            }
        }
    }

    // The PC this one handed the class folders over to (it stood down for it),
    // as the Agents recorded it then, at the generation it took them with;
    // null when it handed none over, or that hand-over no longer counts (it
    // took them over again, or the room named it the home above that
    // generation since). While there is one, this PC takes no room back
    // (ClassFolderHandOver.TakeBackAsync), also once its Master is opened
    // again. Throws if the file can't be read.
    public static ClassHome? ReadHandedOverTo(string? path = null) =>
        Load(path ?? Path).HandedOverTo is { } stored && stored.ToHome() is { } home && IsHome(home) ? home : null;

    // Keeps it (null: none). Throws if it can't be read or written.
    public static void WriteHandedOverTo(ClassHome? home, string? path = null)
    {
        if (home is not null && !IsHome(home))
        {
            throw new ArgumentException("Not a home.", nameof(home));
        }

        path ??= Path;
        lock (Gate)
        {
            var file = Load(path);
            var stored = home is null ? null : StoredHome.Of(home);
            if (file.HandedOverTo != stored)
            {
                Save(path, file with { HandedOverTo = stored });
            }
        }
    }

    // This PC's own generation as the home: the newest of the one on file
    // (saved) and those the Agents keep for this PC (ClassHome.IsSamePc with
    // its certificate thumbprint and computer name: a take-over it made that
    // its file doesn't have yet), never one they keep for another PC; null
    // when it has none.
    public static long? OwnGeneration(long? saved, IEnumerable<ClassHome> homes, string thumbprint, string name)
    {
        var recorded = homes.Where(home => home.Generation > 0 && home.IsSamePc(thumbprint, name, saved))
            .Select(home => (long?)home.Generation).Max();
        return saved is { } kept && recorded is { } fromAgents ? Math.Max(kept, fromAgents) : saved ?? recorded;
    }

    // The generation a takeover claims with: newer than every home the Agents
    // report, and than now (UTC ticks) when this PC's clock is ahead of theirs;
    // never past ClassHome.MaxGeneration, which an Agent refuses.
    public static long TakeOverGeneration(long nowUtcTicks, IEnumerable<ClassHome> reported)
    {
        var newest = reported.Select(home => home.Generation).DefaultIfEmpty(0).Max();
        return Math.Min(ClassHome.MaxGeneration, Math.Max(nowUtcTicks, ClassHome.Next(newest)));
    }

    private static Stored Load(string path)
    {
        string text;
        try
        {
            text = ReadText(path);
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && File.Exists(path))
        {
            Thread.Sleep(RetryWait);
            text = ReadText(path);
        }

        try
        {
            return text.Length > 0 && JsonSerializer.Deserialize<Stored>(text) is { } stored ? stored : None;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return None;
        }
    }

    // Empty when there is no file. FileNotFoundException and the like mean it went meanwhile.
    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return "";
        }
    }

    private static readonly Stored None = new(null, null);

    private static bool IsSamePc(StoredHome stored, ClassHome home) =>
        string.Equals(stored.Thumbprint, home.Thumbprint, StringComparison.OrdinalIgnoreCase)
        && string.Equals(stored.Name, home.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsHome(ClassHome home)
    {
        try
        {
            return ClassHome.TryDecode(home.Encode(), out _);
        }
        catch (InvalidOperationException)
        {
            return false; // what Encode says of a home that can't be one
        }
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

    // HandedOverTo: null in a file from before it was kept (none handed over).
    private sealed record Stored(long? Generation, StoredHome? LastOtherHome, StoredHome? HandedOverTo = null);

    private sealed record StoredHome(string Thumbprint, string Name, string Address, int Port, long Generation)
    {
        public static StoredHome Of(ClassHome home) => new(home.Thumbprint, home.Name, home.Address, home.Port, home.Generation);

        public ClassHome ToHome() => new(Thumbprint, Name, Address, Port, Generation);
    }
}
