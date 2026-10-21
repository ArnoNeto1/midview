using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The lessons map behind ClassStore.RememberLesson and the methods next to it:
// the class each lesson this Master started was of, and whether its end was
// collected (the last round of "Encerrar a aula" ran, so what a PC set aside
// for it afterwards is not wanted back). A PC can come back days later with
// files set aside for a lesson; this is how the Master knows which class they
// belong to. One JSON file, MasterData.Folder\class-lessons.json by default:
// not in the index folder, where a class named "lessons" would have its index.
//
// Never throws: a file that can't be read is set aside (corrupt) or taken as
// empty (unreadable), and one that can't be written is kept in memory and
// written again with the next change; both are logged. The worst that comes
// of it is a PC's leftovers cleared instead of collected, and they go to that
// student's Recycle Bin.
internal sealed class ClassLessonsFile
{
    private readonly string _path;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private Dictionary<Guid, Entry>? _lessons; // loaded on first use

    public ClassLessonsFile(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    public string? TurmaOf(Guid lessonId)
    {
        lock (_gate)
        {
            return Lessons().TryGetValue(lessonId, out var entry) ? entry.Turma : null;
        }
    }

    public bool IsCollected(Guid lessonId)
    {
        lock (_gate)
        {
            return Lessons().TryGetValue(lessonId, out var entry) && entry.Collected;
        }
    }

    public void Remember(Guid lessonId, string turma)
    {
        lock (_gate)
        {
            var lessons = Lessons();
            var collected = lessons.TryGetValue(lessonId, out var known) && known.Collected;
            Change(lessons, lessonId, new Entry(turma, collected));
        }
    }

    // A lesson not remembered is marked too, with no class.
    public void MarkCollected(Guid lessonId)
    {
        lock (_gate)
        {
            var lessons = Lessons();
            var turma = lessons.TryGetValue(lessonId, out var known) ? known.Turma : null;
            Change(lessons, lessonId, new Entry(turma, Collected: true));
        }
    }

    // The class was renamed: its lessons follow it.
    public void RenameClass(string turma, string newName)
    {
        lock (_gate)
        {
            var lessons = Lessons();
            var changed = false;
            foreach (var (lessonId, entry) in lessons.ToList())
            {
                if (string.Equals(entry.Turma, turma, StringComparison.OrdinalIgnoreCase))
                {
                    lessons[lessonId] = entry with { Turma = newName };
                    changed = true;
                }
            }
            if (changed)
            {
                Save(lessons);
            }
        }
    }

    // The class is gone, or a new one takes its name: its lessons are nobody's now.
    public void ForgetClass(string turma)
    {
        lock (_gate)
        {
            var lessons = Lessons();
            var gone = lessons.Where(pair => string.Equals(pair.Value.Turma, turma, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key).ToList();
            foreach (var lessonId in gone)
            {
                lessons.Remove(lessonId);
            }
            if (gone.Count > 0)
            {
                Save(lessons);
            }
        }
    }

    private void Change(Dictionary<Guid, Entry> lessons, Guid lessonId, Entry entry)
    {
        if (lessons.TryGetValue(lessonId, out var known) && known == entry)
        {
            return;
        }
        lessons[lessonId] = entry;
        Save(lessons);
    }

    // With _gate held.
    private Dictionary<Guid, Entry> Lessons() => _lessons ??= Load();

    private Dictionary<Guid, Entry> Load()
    {
        var lessons = new Dictionary<Guid, Entry>();
        if (!File.Exists(_path))
        {
            return lessons;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredLesson?>>(File.ReadAllBytes(_path))
                ?? throw new JsonException("The lessons map is not a list.");
            foreach (var lesson in stored)
            {
                if (lesson is not null && lesson.LessonId != Guid.Empty)
                {
                    lessons[lesson.LessonId] = new Entry(lesson.Turma, lesson.Collected);
                }
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

    // A temp file swapped in, so a crash mid-save never leaves half a map.
    private void Save(Dictionary<Guid, Entry> lessons)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stored = lessons.Select(pair => new StoredLesson(pair.Key, pair.Value.Turma, pair.Value.Collected)).ToList();
            var tempPath = _path + ".tmp";
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(stored));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The lessons map {_path} could not be saved yet: {ex.Message}");
        }
    }

    private sealed record Entry(string? Turma, bool Collected);

    private sealed record StoredLesson(Guid LessonId, string? Turma, bool Collected);
}
