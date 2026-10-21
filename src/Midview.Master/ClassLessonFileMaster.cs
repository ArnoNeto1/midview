using Midview.Protocol;

namespace Midview.Master;

// The lesson the teacher's Master is on ("Pasta da turma", see ClassLesson and
// ClassFolderSync), next to the classroom list, so closing and opening the
// Master carries on the same lesson. The same format and safety as the Agent's
// own copy (ClassLessonFile): written through a temp file, and anything
// unreadable read as no lesson. path: elsewhere than Path (a test's folder).
public static class ClassLessonFileMaster
{
    public static string Path => System.IO.Path.Combine(MasterData.Folder, "class-lesson.json");

    public static ClassLesson? Read(string? path = null) => ClassLessonFile.Read(path ?? Path);

    // Throws if it can't be written.
    public static void Write(ClassLesson lesson, string? path = null) => ClassLessonFile.Write(path ?? Path, lesson);
}
