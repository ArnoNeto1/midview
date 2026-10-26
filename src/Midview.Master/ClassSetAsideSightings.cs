using System.IO;
using System.Text.Json;

namespace Midview.Master;

// When this Master first saw, on each PC, files set aside for a lesson it
// doesn't know (ClassFolderSync.SetAside.cs: they are cleared some time after
// that): a file's own time can't say, since one copied from a USB stick or a
// zip, or the teacher's own material, keeps an old one. Kept apart from the
// lessons map (which says which lessons are known, and goes with the folders
// at a take-over), in a small JSON file next to it. A PC's entry for a lesson
// goes once that lesson is known, or its files are gone from that PC, and at
// most MaxEntries are kept: the newest. Never throws: a file that can't be
// read is taken as empty (each lesson's clock starts again, so its files go
// later, never sooner), and one that can't be written is written again with
// the next change; both are logged.
internal sealed class ClassSetAsideSightings
{
    public const int MaxEntries = 10_000;

    private readonly string _path;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private Dictionary<(string Machine, Guid LessonId), long>? _seen; // loaded on first use

    public ClassSetAsideSightings(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    // For the PC with that machine key: the lessons it has set-aside files of
    // that this PC doesn't know now (unknown), each seen first now unless it
    // was seen before; its other entries go. When each was first seen (UTC ticks).
    public IReadOnlyDictionary<Guid, long> Note(string machine, IReadOnlyCollection<Guid> unknown, long nowUtcTicks)
    {
        var key = machine.ToUpperInvariant();
        lock (_gate)
        {
            var seen = Seen();
            var changed = false;
            foreach (var gone in seen.Keys.Where(entry => entry.Machine == key && !unknown.Contains(entry.LessonId)).ToList())
            {
                changed |= seen.Remove(gone);
            }
            var firstSeen = new Dictionary<Guid, long>();
            foreach (var lessonId in unknown)
            {
                if (!seen.TryGetValue((key, lessonId), out var at))
                {
                    at = nowUtcTicks;
                    seen[(key, lessonId)] = at;
                    changed = true;
                }
                firstSeen[lessonId] = at;
            }
            if (seen.Count > MaxEntries)
            {
                foreach (var oldest in seen.OrderBy(entry => entry.Value).Take(seen.Count - MaxEntries).Select(entry => entry.Key).ToList())
                {
                    seen.Remove(oldest);
                }
            }
            if (changed)
            {
                Save(seen);
            }
            return firstSeen;
        }
    }

    // When the PC with that machine key was first seen with files set aside
    // for that lesson (UTC ticks); null when it wasn't (Note says so).
    public long? FirstSeen(string machine, Guid lessonId)
    {
        lock (_gate)
        {
            return Seen().TryGetValue((machine.ToUpperInvariant(), lessonId), out var at) ? at : null;
        }
    }

    // With _gate held.
    private Dictionary<(string Machine, Guid LessonId), long> Seen() => _seen ??= Load();

    private Dictionary<(string Machine, Guid LessonId), long> Load()
    {
        var seen = new Dictionary<(string Machine, Guid LessonId), long>();
        try
        {
            if (!File.Exists(_path))
            {
                return seen;
            }
            foreach (var entry in JsonSerializer.Deserialize<List<Sighting?>>(File.ReadAllBytes(_path)) ?? new())
            {
                if (entry?.Machine is { Length: > 0 } machine && entry.LessonId != Guid.Empty)
                {
                    seen[(machine.ToUpperInvariant(), entry.LessonId)] = entry.FirstSeen;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            _log($"When files set aside for unknown lessons were first seen could not be read ({_path}): {ex.Message}");
            seen.Clear();
        }
        return seen;
    }

    // With _gate held. A temp file swapped in, so a crash mid-save never leaves half of it.
    private void Save(Dictionary<(string Machine, Guid LessonId), long> seen)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var tempPath = _path + ".tmp";
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(
                seen.Select(entry => new Sighting(entry.Key.Machine, entry.Key.LessonId, entry.Value)).ToList()));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"When files set aside for unknown lessons were first seen could not be saved yet ({_path}): {ex.Message}");
        }
    }

    private sealed record Sighting(string? Machine, Guid LessonId, long FirstSeen);
}
