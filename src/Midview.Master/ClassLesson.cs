using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// The lesson going on now, from "Iniciar aula" to "Encerrar aula": which
// class, and what each machine's "Pasta da turma" held the last time it was
// loaded or saved (by its Session thumbprint) -- what tells a file the
// student changed or deleted from one they didn't touch. Kept in a file
// (ClassLessonFile), so closing and opening the teacher's program carries on
// with the same lesson.
public sealed class ClassLesson
{
    public required string ClassName { get; init; }

    public required Guid Id { get; init; }

    public required DateTime StartedAtUtc { get; init; }

    public Dictionary<string, List<ClassFolderEntry>> Machines { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // The paths this lesson added to the teacher's copy: what "Apagar só os
    // de hoje" removes.
    public HashSet<string> Added { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static ClassLesson Start(string className, DateTime nowUtc) =>
        new() { ClassName = className, Id = Guid.NewGuid(), StartedAtUtc = nowUtc };
}

public static class ClassLessonFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    public static string DefaultPath => Path.Combine(MasterData.Folder, "aula-em-andamento.json");

    // Null when no lesson is going on. An unreadable file is moved aside to
    // "<path>.corrupt", like the roster, and counts as no lesson: the class's
    // files are all in the teacher's copy anyway.
    public static ClassLesson? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var lesson = JsonSerializer.Deserialize<ClassLesson>(File.ReadAllText(path));
            if (lesson is null || lesson.Id == Guid.Empty || string.IsNullOrWhiteSpace(lesson.ClassName))
            {
                throw new JsonException("aula incompleta");
            }
            return new ClassLesson
            {
                ClassName = lesson.ClassName,
                Id = lesson.Id,
                StartedAtUtc = lesson.StartedAtUtc,
                Machines = new Dictionary<string, List<ClassFolderEntry>>(
                    lesson.Machines.Where(pair => pair.Value is not null)
                        .Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Where(entry => entry?.Path is not null).ToList())),
                    StringComparer.OrdinalIgnoreCase),
                Added = new HashSet<string>(lesson.Added.Where(path => path is not null), StringComparer.OrdinalIgnoreCase),
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or NullReferenceException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return null;
        }
    }

    // Temp file then swap, like KnownMachinesFile.
    public static void Save(string path, ClassLesson lesson)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, lesson, WriteOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
