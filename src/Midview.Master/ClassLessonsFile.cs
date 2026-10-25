using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The lessons map behind ClassStore.RememberLesson and the methods next to it:
// the class each lesson this Master started was of, whether its end was
// collected (the last round of "Encerrar a aula" ran, so what a PC set aside
// for it afterwards is not wanted back), and, for a lesson that ended without
// those last rounds, the versions of files the class already had or gave out
// then, so what a PC set aside that is one of them is not brought back. A PC
// can come back days later with files set aside for a lesson; this is how the
// Master knows which class they belong to and which are new. One JSON file,
// MasterData.Folder\class-lessons.json by default: not in the index folder,
// where a class named "lessons" would have its index.
//
// Never throws: a file that can't be read is set aside (corrupt) or taken as
// empty (unreadable), and one that can't be written is kept in memory and
// written again with the next change; both are logged. The worst that comes
// of it is a PC's leftovers cleared instead of collected (they go to that
// student's Recycle Bin), or collected as a "(2)" of what the class has.
internal sealed class ClassLessonsFile
{
    private readonly string _path;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private Dictionary<Guid, Lesson>? _lessons; // loaded on first use

    public ClassLessonsFile(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    // Where the map is (or would be).
    public string Path => _path;

    // The file was replaced or moved away (a hand-over of the class folders,
    // ClassStore.HandOver.cs): what is in memory is forgotten, and the file is
    // read again at the next use.
    public void Reload()
    {
        lock (_gate)
        {
            _lessons = null;
        }
    }

    public string? TurmaOf(Guid lessonId)
    {
        lock (_gate)
        {
            return Lessons().TryGetValue(lessonId, out var lesson) ? lesson.Turma : null;
        }
    }

    public bool IsCollected(Guid lessonId)
    {
        lock (_gate)
        {
            return Lessons().TryGetValue(lessonId, out var lesson) && lesson.Collected;
        }
    }

    // Paths compare ignoring case.
    public bool IsKnown(Guid lessonId, string path, FileVersion version)
    {
        lock (_gate)
        {
            return Lessons().TryGetValue(lessonId, out var lesson) && lesson.Known.Contains(KnownOf(path, version));
        }
    }

    public void Remember(Guid lessonId, string turma)
    {
        lock (_gate)
        {
            var lesson = LessonOf(lessonId);
            if (lesson.Turma != turma)
            {
                lesson.Turma = turma;
                Save();
            }
        }
    }

    // A lesson not remembered is marked too, with no class.
    public void MarkCollected(Guid lessonId)
    {
        lock (_gate)
        {
            var lesson = LessonOf(lessonId);
            if (!lesson.Collected)
            {
                lesson.Collected = true;
                Save();
            }
        }
    }

    public void AddKnown(Guid lessonId, IEnumerable<(string Path, FileVersion Version)> versions)
    {
        lock (_gate)
        {
            var known = LessonOf(lessonId).Known;
            var added = false;
            foreach (var (path, version) in versions)
            {
                added |= known.Add(KnownOf(path, version));
            }
            if (added)
            {
                Save();
            }
        }
    }

    // The class was renamed: its lessons follow it.
    public void RenameClass(string turma, string newName)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var lesson in Lessons().Values.Where(lesson => SameClass(lesson.Turma, turma)))
            {
                lesson.Turma = newName;
                changed = true;
            }
            if (changed)
            {
                Save();
            }
        }
    }

    // The class is gone, or a new one takes its name: its lessons are nobody's now.
    public void ForgetClass(string turma)
    {
        lock (_gate)
        {
            var lessons = Lessons();
            var gone = lessons.Where(pair => SameClass(pair.Value.Turma, turma)).Select(pair => pair.Key).ToList();
            foreach (var lessonId in gone)
            {
                lessons.Remove(lessonId);
            }
            if (gone.Count > 0)
            {
                Save();
            }
        }
    }

    private static bool SameClass(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static (string Path, long Size, long LastWriteTicks) KnownOf(string path, FileVersion version) =>
        (path.ToUpperInvariant(), version.Size, version.LastWriteTicks);

    // With _gate held.
    private Lesson LessonOf(Guid lessonId)
    {
        var lessons = Lessons();
        if (!lessons.TryGetValue(lessonId, out var lesson))
        {
            lesson = new Lesson();
            lessons[lessonId] = lesson;
        }
        return lesson;
    }

    // With _gate held.
    private Dictionary<Guid, Lesson> Lessons() => _lessons ??= Load();

    private Dictionary<Guid, Lesson> Load()
    {
        var lessons = new Dictionary<Guid, Lesson>();
        if (!File.Exists(_path))
        {
            return lessons;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredLesson?>>(File.ReadAllBytes(_path))
                ?? throw new JsonException("The lessons map is not a list.");
            foreach (var entry in stored)
            {
                if (entry is null || entry.LessonId == Guid.Empty)
                {
                    continue;
                }

                var lesson = new Lesson { Turma = entry.Turma, Collected = entry.Collected };
                foreach (var known in entry.Known ?? new())
                {
                    if (known?.Path is { } path)
                    {
                        lesson.Known.Add(KnownOf(path, new FileVersion(known.Size, known.LastWriteTicks)));
                    }
                }
                lessons[entry.LessonId] = lesson;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            _log($"The lessons map {_path} is not readable and was set aside: {ex.Message}");
            try
            {
                File.Move(_path, _path + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Written over at the next change.
            }
            lessons.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The lessons map {_path} could not be read: {ex.Message}");
            lessons.Clear();
        }
        return lessons;
    }

    // With _gate held. A temp file swapped in, so a crash mid-save never leaves half a map.
    private void Save()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stored = Lessons().Select(pair => new StoredLesson(
                pair.Key, pair.Value.Turma, pair.Value.Collected,
                pair.Value.Known.Count == 0
                    ? null
                    : pair.Value.Known.Select(known => (StoredVersion?)new StoredVersion(known.Path, known.Size, known.LastWriteTicks)).ToList()))
                .ToList();
            var tempPath = _path + ".tmp";
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(stored));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The lessons map {_path} could not be saved yet: {ex.Message}");
        }
    }

    private sealed class Lesson
    {
        public string? Turma { get; set; }

        public bool Collected { get; set; }

        // Paths upper-cased (invariant), so they compare ignoring case.
        public HashSet<(string Path, long Size, long LastWriteTicks)> Known { get; } = new();
    }

    private sealed record StoredLesson(Guid LessonId, string? Turma, bool Collected, List<StoredVersion?>? Known);

    private sealed record StoredVersion(string? Path, long Size, long LastWriteTicks);
}
