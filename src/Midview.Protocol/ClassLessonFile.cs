using System.Text.Json;

namespace Midview.Protocol;

// %ProgramData%\Midview\class-lesson.json: the lesson the class folder
// follows (see ClassLesson), as one JSON object. The Agent writes it when the
// teacher starts or ends a lesson; the Session reads it to know which folder
// to keep on the student's Desktop (Users can read %ProgramData%\Midview).
public static class ClassLessonFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Midview", "class-lesson.json");

    // Null when there is no lesson on file: missing, unreadable or corrupt.
    // Never throws, and never touches the file: the Session reads it while
    // the Agent may be replacing it, and the Agent is the one that decides
    // what a bad file means.
    public static ClassLesson? Read(string path)
    {
        try
        {
            // Shared with a writer that replaces the file under us.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var stored = JsonSerializer.Deserialize<StoredLesson>(stream);
            if (stored is null)
            {
                return null;
            }

            var lesson = new ClassLesson(stored.SavedAt, stored.LessonId, stored.Turma, stored.StartedAt, stored.Ended);
            return lesson.IsWellFormed ? lesson : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Written to a temporary file and moved over the old one, so a reader
    // never sees half a lesson. Throws if it can't be written.
    public static void Write(string path, ClassLesson lesson)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new StoredLesson(
                lesson.SavedAt, lesson.LessonId, lesson.Turma, lesson.StartedAt, lesson.Ended));
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record StoredLesson(long SavedAt, Guid LessonId, string Turma, long StartedAt, bool Ended);
}
