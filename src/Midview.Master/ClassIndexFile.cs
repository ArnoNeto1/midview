using System.IO;
using System.Text.Json;

namespace Midview.Master;

// What the Master remembers about one class besides its files (see
// ClassStore): when each file arrived, which files the teacher deleted, and
// which version of each file every PC last had. Paths are '/'-separated and
// compare ignoring case, as on the disk; so do the machine keys (thumbprints).
internal sealed class ClassIndex
{
    // The files of the class the index knows, with the day each arrived
    // (local date). Disjoint from Deleted.
    public Dictionary<string, DateOnly> ArrivedOn { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Tombstones: files the index knew that the teacher then deleted in
    // Explorer, so the PCs still holding them are told to drop them.
    public HashSet<string> Deleted { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Machine key -> path -> the version that PC last got from, or gave to,
    // the store.
    public Dictionary<string, Dictionary<string, FileVersion>> Machines { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Changed in memory (the per-PC records) and not saved yet.
    public bool Dirty { get; set; }
}

// "<indexRoot>\<turma>.json", saved like KnownMachinesFile: a temp file
// swapped in, so a crash mid-save never leaves a truncated index, and an
// unreadable one set aside rather than lost.
internal static class ClassIndexFile
{
    // The shape on disk. Every part is optional, so a hand-edited or older
    // file still loads; the ones that matter are rebuilt case-insensitive.
    private sealed class Stored
    {
        public Dictionary<string, DateOnly>? ArrivedOn { get; set; }

        public List<string?>? Deleted { get; set; }

        public Dictionary<string, Dictionary<string, FileVersion>?>? Machines { get; set; }
    }

    // A file that isn't a readable index is moved to "<path>.corrupt"
    // (returned through corruptCopyPath) and the class starts with an empty
    // one: the files are the class's work and are still there to be listed,
    // the index is only what Midview knows about them.
    //
    // A file that merely can't be opened (in use) throws instead of being
    // taken for corrupt: starting over on top of a good index would lose it.
    public static ClassIndex Load(string path, out string? corruptCopyPath)
    {
        corruptCopyPath = null;
        if (!File.Exists(path))
        {
            return new ClassIndex();
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllBytes(path))
                ?? throw new JsonException("The index is not an object.");
            var index = new ClassIndex();
            foreach (var (file, day) in stored.ArrivedOn ?? new())
            {
                index.ArrivedOn[file] = day;
            }
            foreach (var file in stored.Deleted ?? new())
            {
                if (file is not null)
                {
                    index.Deleted.Add(file);
                }
            }
            foreach (var (machine, records) in stored.Machines ?? new())
            {
                if (records is null)
                {
                    continue;
                }

                var kept = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
                foreach (var (file, version) in records)
                {
                    kept[file] = version;
                }
                index.Machines[machine] = kept;
            }
            return index;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException
            or NotSupportedException or ArgumentException)
        {
            corruptCopyPath = path + ".corrupt";
            File.Move(path, corruptCopyPath, overwrite: true);
            return new ClassIndex();
        }
    }

    public static void Save(string path, ClassIndex index)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stored = new Stored
        {
            ArrivedOn = index.ArrivedOn,
            Deleted = index.Deleted.Cast<string?>().ToList(),
            Machines = index.Machines.ToDictionary(m => m.Key, m => (Dictionary<string, FileVersion>?)m.Value),
        };

        // Compact, not indented: with a PC's record per file this gets big.
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, stored);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}
