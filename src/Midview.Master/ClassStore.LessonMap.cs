using System.IO;

namespace Midview.Master;

// ClassStore: which class each lesson was of (see ClassLessonsFile), for the
// files a PC set aside when a lesson ended without this Master collecting them.
public sealed partial class ClassStore
{
    private readonly ClassLessonsFile _lessons;

    // Where the lessons map is for DefaultIndexRoot: next to the index folder, not in it.
    public static string DefaultLessonsPath => Path.Combine(MasterData.Folder, "class-lessons.json");

    // Throws ArgumentException for a name that is no class name.
    public void RememberLesson(Guid lessonId, string turma)
    {
        FolderOf(turma);
        _lessons.Remember(lessonId, turma);
    }

    // Null for a lesson this Master never started, or whose class was deleted
    // (a class renamed since takes its lessons with it).
    public string? TurmaOfLesson(Guid lessonId) => _lessons.TurmaOf(lessonId);

    // The lesson's end was collected: what a PC set aside for it afterwards is
    // its own (it goes to that student's Recycle Bin), never the class's.
    public void MarkCollected(Guid lessonId) => _lessons.MarkCollected(lessonId);

    public bool IsCollected(Guid lessonId) => _lessons.IsCollected(lessonId);

    // For a lesson about to end without its last rounds (and so not collected),
    // before EndLesson clears the tombstones and the per-PC records: every
    // version of a file the class holds, and every version a PC is known to
    // have had. What a PC then sets aside that is one of them is the class's
    // already, or something the teacher deleted, and is not brought back.
    // Throws ArgumentException for a name that is no class name.
    public void RememberVersionsOf(Guid lessonId, string turma)
    {
        var known = new List<(string Path, FileVersion Version)>();
        try
        {
            known.AddRange(Files(turma).Select(file => (file.Key, file.Value)));
        }
        catch (IOException ex)
        {
            _log($"The files of {turma} could not all be read to remember them: {ex.Message}");
        }

        lock (_gate)
        {
            try
            {
                foreach (var records in IndexOf(turma).Machines.Values)
                {
                    known.AddRange(records.Select(record => (record.Key, record.Value)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"The index of {turma} could not be read to remember its versions: {ex.Message}");
            }
        }
        _lessons.AddKnown(lessonId, known);
    }

    // A file a PC set aside for that lesson, now in the class.
    public void RememberVersion(Guid lessonId, string path, FileVersion version) =>
        _lessons.AddKnown(lessonId, new[] { (path, version) });

    // Whether that version of the path is one RememberVersionsOf or RememberVersion
    // noted for the lesson (paths compare ignoring case).
    public bool IsKnownVersion(Guid lessonId, string path, FileVersion version) =>
        _lessons.IsKnown(lessonId, path, version);

    // The map beside the index folder unless told otherwise: for DefaultIndexRoot
    // that is DefaultLessonsPath, and a test's index folder keeps its map with it.
    private static ClassLessonsFile LessonsMap(string indexRoot, string? lessonsPath, Action<string> log) =>
        new(lessonsPath ?? Path.Combine(Path.GetDirectoryName(indexRoot) ?? indexRoot, "class-lessons.json"), log);
}
