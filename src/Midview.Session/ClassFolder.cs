using Midview.Protocol;

namespace Midview.Session;

// The lesson a "Pasta da turma" holds: which, of what class, and when the
// teacher's PC last loaded or saved it.
public sealed record ClassFolderLesson(Guid Id, string ClassName, DateTime LastActivityUtc);

// How preparing for a lesson went: the indexes of the files still wanted,
// what couldn't be taken out, and whether the folder changed lessons. When
// Refusal is set nothing was touched: another lesson is still going on here.
public sealed record ClassFolderPreparation(
    IReadOnlyList<int> Wanted, IReadOnlyList<string> Problems, bool NewLesson, string? Refusal = null);

// The "Pasta da turma" on the student's Desktop: this lesson's copy of the
// class's folder, which the teacher's PC loads, saves as it changes and
// empties when the lesson ends (ClassFolderSync). Which lesson it holds is
// kept outside it, in the student's LocalAppData, so nothing in the folder
// can be deleted to the same effect.
//
// What leaves the folder goes to the student's Recycle Bin, never straight
// away: the teacher's PC may not have saved it (a lesson left open, a machine
// that was off when the lesson ended). Only what's inside the folder is ever
// touched; a linked folder (junction, symbolic link) is never followed, only
// recycled as a link.
public sealed class ClassFolder
{
    // A lesson saved more recently than this is still going on: another
    // teacher's program (a colleague in the same room) can't load its own
    // over it. One left behind -- its teacher's program closed, the machine
    // off when it ended -- gives way after this long.
    public static readonly TimeSpan ActiveLessonWindow = TimeSpan.FromMinutes(5);

    private readonly IRecycleBin _recycleBin;

    public ClassFolder(string directory, string lessonFilePath, IRecycleBin recycleBin)
    {
        DirectoryPath = Path.GetFullPath(directory);
        LessonFilePath = lessonFilePath;
        _recycleBin = recycleBin;
    }

    // Session runs as the student, so these are their own.
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StudentFolderNames.ClassFolder);

    public static string DefaultLessonFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Midview", "pasta-da-turma.txt");

    public string DirectoryPath { get; }

    public string LessonFilePath { get; }

    // One exchange at a time: a load and a save of the same folder never
    // run together.
    public object Gate { get; } = new();

    public void TryCreate()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not create the class folder {DirectoryPath}: {ex.Message}");
        }
    }

    // The lesson the folder holds, or null when none was loaded (or the
    // record can't be read). Its file's time is when it was last used.
    public ClassFolderLesson? CurrentLesson()
    {
        try
        {
            var lines = File.ReadAllLines(LessonFilePath);
            return lines.Length > 0 && Guid.TryParse(lines[0].Trim(), out var id)
                ? new ClassFolderLesson(id, lines.Length > 1 ? lines[1] : string.Empty, File.GetLastWriteTimeUtc(LessonFilePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The teacher's PC just loaded or saved the lesson: it's still going on.
    public void Touch(DateTime nowUtc)
    {
        try
        {
            File.SetLastWriteTimeUtc(LessonFilePath, nowUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // See ClassFolderFiles.List.
    public IReadOnlyList<ClassFolderFile> ListFiles() => ClassFolderFiles.List(DirectoryPath);

    // Before loading a lesson. The same lesson again (the teacher's program
    // restarted) takes nothing out: what's there may be work not saved yet.
    // Another lesson still going on (ActiveLessonWindow) is left alone and
    // the load refused. Otherwise everything in the folder that isn't
    // exactly one of the lesson's files goes to the Recycle Bin, and the
    // folder becomes this lesson's.
    public ClassFolderPreparation Prepare(
        Guid lesson, string className, IReadOnlyList<ClassFolderEntry> manifest, DateTime nowUtc)
    {
        Directory.CreateDirectory(DirectoryPath);
        var problems = new List<string>();
        var current = CurrentLesson();
        var newLesson = current?.Id != lesson;
        if (newLesson && current is not null && nowUtc - current.LastActivityUtc < ActiveLessonWindow)
        {
            var other = current.ClassName.Length > 0 ? $" (turma {current.ClassName})" : string.Empty;
            return new ClassFolderPreparation(Array.Empty<int>(), problems, NewLesson: false,
                Refusal: $"outra aula está em andamento nesta máquina{other}; tente de novo quando ela for encerrada");
        }

        var present = ListFiles().ToDictionary(file => file.Entry.Path, StringComparer.OrdinalIgnoreCase);
        if (newLesson)
        {
            var keep = new HashSet<string>(
                manifest.Where(entry => present.TryGetValue(entry.Path, out var file) && file.Entry.SameVersionAs(entry))
                    .Select(entry => entry.Path),
                StringComparer.OrdinalIgnoreCase);
            RecycleAllBut(new DirectoryInfo(DirectoryPath), new List<string>(), keep, problems);
            present = ListFiles().ToDictionary(file => file.Entry.Path, StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(LessonFilePath)!);
            File.WriteAllLines(LessonFilePath, new[] { lesson.ToString(), className });
        }
        Touch(nowUtc);

        var wanted = new List<int>();
        for (var i = 0; i < manifest.Count; i++)
        {
            if (!present.ContainsKey(manifest[i].Path) && TargetPath(manifest[i].Path) is not null)
            {
                wanted.Add(i);
            }
        }
        return new ClassFolderPreparation(wanted, problems, newLesson);
    }

    // See ClassFolderFiles.TargetPath.
    public string? TargetPath(string relativePath) => ClassFolderFiles.TargetPath(DirectoryPath, relativePath);

    // A file from the teacher's copy: written beside its place, then moved
    // over whatever is there, with the time it has on the teacher's PC.
    public IncomingClassFile Begin(string relativePath, long lastWriteTicks)
    {
        var target = TargetPath(relativePath) ?? throw new IOException($"caminho inválido: {relativePath}");
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, ClassFolderFiles.NewTempName());
        return new IncomingClassFile(tempPath, target, lastWriteTicks);
    }

    // At the end of the lesson: everything in the folder goes to the
    // student's Recycle Bin, and the lesson is forgotten.
    public (int Recycled, IReadOnlyList<string> Problems) Clear()
    {
        var problems = new List<string>();
        var recycled = 0;
        if (Directory.Exists(DirectoryPath))
        {
            recycled = RecycleAllBut(new DirectoryInfo(DirectoryPath), new List<string>(), new HashSet<string>(), problems);
        }
        try
        {
            File.Delete(LessonFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"registro da aula ({ErrorText.Describe(ex)})");
        }
        return (recycled, problems);
    }

    // Recycles everything under directory except the files in keep (paths
    // relative to the folder), hidden ones included: a file or a whole
    // subfolder at a time, a folder holding something kept only by its
    // contents. Returns how many items were recycled.
    private int RecycleAllBut(DirectoryInfo directory, List<string> segments, HashSet<string> keep, List<string> problems)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Describe(segments, directory.Name)} ({ErrorText.Describe(ex)})");
            return 0;
        }

        var recycled = 0;
        foreach (var entry in entries)
        {
            var path = Describe(segments, entry.Name);
            var isFolder = entry is DirectoryInfo && (entry.Attributes & FileAttributes.ReparsePoint) == 0;
            try
            {
                if (isFolder && keep.Any(kept => kept.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    segments.Add(entry.Name);
                    recycled += RecycleAllBut((DirectoryInfo)entry, segments, keep, problems);
                    segments.RemoveAt(segments.Count - 1);
                }
                else if (isFolder || !keep.Contains(path))
                {
                    _recycleBin.Recycle(entry.FullName);
                    recycled++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{path} ({ErrorText.Describe(ex)})");
            }
        }
        return recycled;
    }

    private static string Describe(List<string> segments, string name) =>
        ClassFolderSync.PathOf(segments.Append(name).ToList());
}

// One file being written into the class folder: Write the bytes, then
// Complete puts it in place. Disposing without completing leaves nothing.
public sealed class IncomingClassFile : IDisposable
{
    private readonly string _tempPath;
    private readonly string _target;
    private readonly long _lastWriteTicks;
    private readonly FileStream _stream;
    private bool _completed;

    internal IncomingClassFile(string tempPath, string target, long lastWriteTicks)
    {
        _tempPath = tempPath;
        _target = target;
        _lastWriteTicks = lastWriteTicks;
        _stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        File.SetAttributes(tempPath, FileAttributes.Hidden);
    }

    public void Write(byte[] data) => _stream.Write(data);

    public void Complete()
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        File.SetAttributes(_tempPath, FileAttributes.Normal);
        File.SetLastWriteTimeUtc(_tempPath, new DateTime(_lastWriteTicks, DateTimeKind.Utc));
        if (Directory.Exists(_target))
        {
            throw new IOException($"já existe uma pasta com o nome {Path.GetFileName(_target)}");
        }
        File.Move(_tempPath, _target, overwrite: true);
        _completed = true;
    }

    public void Dispose()
    {
        _stream.Dispose();
        if (!_completed)
        {
            try
            {
                File.Delete(_tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
