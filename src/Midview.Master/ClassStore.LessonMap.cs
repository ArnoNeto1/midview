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

    // The map beside the index folder unless told otherwise: for DefaultIndexRoot
    // that is DefaultLessonsPath, and a test's index folder keeps its map with it.
    private static ClassLessonsFile LessonsMap(string indexRoot, string? lessonsPath, Action<string> log) =>
        new(lessonsPath ?? Path.Combine(Path.GetDirectoryName(indexRoot) ?? indexRoot, "class-lessons.json"), log);
}
