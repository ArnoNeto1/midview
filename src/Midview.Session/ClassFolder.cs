using System.Text;
using Midview.Protocol;

namespace Midview.Session;

// The class folder ("Pasta da turma", see docs/superpowers/specs/
// 2026-10-20-class-folder-design.md) as the student's own Session sees it: the
// "Pasta da turma – <turma>" folder on the Desktop while a lesson is on, and
// the folders of finished lessons whose files were set aside (one per lesson
// id, under setAsideRoot) until the teacher's Master has them. All file reading
// and writing happens here, as the student, never in the Agent.
//
// Every path the other side sends is untrusted: it goes through
// FileCollection.SafeRelativePath, is joined to the lesson's folder, must stay
// inside it, and may not pass through a link (junction or symbolic link).
// Refusals that the caller should word for the teacher are an
// InvalidOperationException (BeginPush) or a (false, reason) result.
public sealed class ClassFolder
{
    // A file still being written, or one of Midview's own temp files, is never
    // listed (and so never copied half-written): Chrome's ".crdownload", an
    // Office or editor ".tmp", a ".part" and Midview's ".parcial".
    public static readonly string[] SkippedSuffixes = { ".crdownload", ".tmp", ".parcial", ".part" };

    private const string OtherLesson = "aula diferente";
    private const string NoFolder = "sem pasta";
    private const string Linked = "pasta vinculada";

    // Hidden and system entries (Office's "~$" lock files, say) and links are skipped.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private readonly string _desktopDirectory;
    private readonly string _setAsideRoot;
    private readonly Func<ClassLesson?> _agentLesson;
    private readonly IRecycleBin _recycleBin;
    private readonly int _maxEntries;

    // desktopDirectory: the student's Desktop ("" when unknown). setAsideRoot:
    // where a finished lesson's files wait, one folder per lesson id.
    // agentLesson: the lesson the Agent says is on. sessionStatePath: where
    // the lesson this Session last followed is remembered.
    public ClassFolder(
        string desktopDirectory,
        string setAsideRoot,
        Func<ClassLesson?> agentLesson,
        IRecycleBin recycleBin,
        string sessionStatePath,
        int maxEntries = ClassFolderWire.MaxEntries)
    {
        _desktopDirectory = desktopDirectory;
        _setAsideRoot = setAsideRoot;
        _agentLesson = agentLesson;
        _recycleBin = recycleBin;
        SessionStatePath = sessionStatePath;
        _maxEntries = maxEntries;
    }

    public string SessionStatePath { get; }

    // The files of the current lesson's Desktop folder (LessonId = the current
    // lesson) and of every set-aside lesson (LessonId = that lesson), at most
    // ClassFolderWire.MaxEntries, files before subfolders and in name order,
    // the current lesson first. Left out: what SkippedSuffixes, "~$", Midview's
    // ".midview-" prefix, hidden or system attributes and links say, anything
    // deeper than FileCollection.MaxDepth, and anything the wire format or the
    // other messages couldn't carry (a file over FileTransfer.MaxFileSize, a
    // path over FileTransfer.MaxNameBytes of UTF-8, a name that
    // FileCollection.SafeRelativePath would rewrite), so one such file never
    // spoils the listing.
    //
    // CurrentLessonId is Guid.Empty with no lesson on. Problem is "sem pasta"
    // when a lesson is on but its folder isn't there (yet) or the Desktop is
    // unknown, "pasta vinculada" when the folder is a link; a listing with a
    // problem has no entries at all, set-aside ones included (ClassFolderWire).
    public (Guid CurrentLessonId, string? Problem, IReadOnlyList<ClassFolderEntry> Entries) List()
    {
        var lesson = CurrentLesson();
        var entries = new List<ClassFolderEntry>();

        if (lesson is not null)
        {
            var (folder, reason) = FindFolder(lesson, lesson.LessonId, onlyCurrent: true);
            if (folder is null)
            {
                return (lesson.LessonId, reason, Array.Empty<ClassFolderEntry>());
            }
            Walk(new DirectoryInfo(folder), lesson.LessonId, prefix: string.Empty, depth: 1, entries);
        }

        foreach (var (lessonId, directory) in SetAsideFolders())
        {
            if (entries.Count >= _maxEntries)
            {
                break;
            }

            // LessonId equal to the current lesson means "in the Desktop folder".
            if (lessonId != lesson?.LessonId)
            {
                Walk(directory, lessonId, prefix: string.Empty, depth: 1, entries);
            }
        }
        return (lesson?.LessonId ?? Guid.Empty, null, entries);
    }

    // The full path of an existing file of that lesson's folder (the Desktop
    // one for the current lesson, the set-aside one for any other), or null
    // when there is no such lesson, the path doesn't lead to a file inside it,
    // or a link is on the way.
    public string? ResolveExisting(Guid lessonId, string relativePath)
    {
        var (folder, _) = FindFolder(CurrentLesson(), lessonId, onlyCurrent: false);
        return folder is null ? null : ExistingFile(folder, relativePath);
    }

    // Starts receiving a file into the current lesson's Desktop folder, in
    // subfolders it makes as needed. Nothing shows under the real name until
    // Complete. Throws InvalidOperationException with the reason for the
    // teacher ("aula diferente" for a lesson that isn't the current one,
    // "sem pasta" when the Agent hasn't made the folder, a link on the way, a
    // folder where the file would go); IO errors come as IOException.
    public ClassFolderIncoming BeginPush(Guid lessonId, string relativePath)
    {
        var (folder, reason) = FindFolder(CurrentLesson(), lessonId, onlyCurrent: true);
        if (folder is null)
        {
            throw new InvalidOperationException(reason);
        }

        var target = Inside(folder, FileCollection.SafeRelativePath(relativePath))
            ?? throw new InvalidOperationException("caminho não permitido");
        if (Directory.Exists(target))
        {
            throw new InvalidOperationException("já existe uma pasta com esse nome");
        }

        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".midview-{Guid.NewGuid():N}.parcial");
        return new ClassFolderIncoming(target, tempPath);
    }

    // To the student's Recycle Bin, so it can be undone.
    public (bool Ok, string Detail) Delete(Guid lessonId, string relativePath)
    {
        var (folder, reason) = FindFolder(CurrentLesson(), lessonId, onlyCurrent: false);
        if (folder is null)
        {
            return (false, reason);
        }

        var file = ExistingFile(folder, relativePath);
        if (file is null)
        {
            return (false, "arquivo não encontrado");
        }

        try
        {
            _recycleBin.Recycle(file);
            return (true, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ErrorText.Describe(ex));
        }
    }

    // Within the lesson's folder; fails, changing nothing, if the new name
    // is taken (by a file or a folder) or its folder isn't there.
    public (bool Ok, string Detail) Rename(Guid lessonId, string relativePath, string newRelativePath)
    {
        var (folder, reason) = FindFolder(CurrentLesson(), lessonId, onlyCurrent: false);
        if (folder is null)
        {
            return (false, reason);
        }

        var file = ExistingFile(folder, relativePath);
        if (file is null)
        {
            return (false, "arquivo não encontrado");
        }

        var target = Inside(folder, FileCollection.SafeRelativePath(newRelativePath));
        if (target is null)
        {
            return (false, "caminho não permitido");
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return (false, "já existe um arquivo com esse nome");
        }

        try
        {
            File.Move(file, target);
            return (true, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ErrorText.Describe(ex));
        }
    }

    // The teacher's Master has that lesson's set-aside files: the folder goes
    // to the Recycle Bin. Nothing set aside is nothing to clear, so it's Ok.
    public (bool Ok, string Detail) ClearSetAside(Guid lessonId)
    {
        var folder = SetAsideFolder(lessonId);
        if (folder is null || !Directory.Exists(folder))
        {
            return (true, string.Empty);
        }

        if (IsLink(folder))
        {
            return (false, "pasta vinculada, não removida");
        }

        try
        {
            _recycleBin.Recycle(folder);
            return (true, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ErrorText.Describe(ex));
        }
    }

    // The lesson the Agent says is on; null when none is.
    private ClassLesson? CurrentLesson() => _agentLesson() is { IsOn: true } lesson ? lesson : null;

    // The Desktop folder of the lesson. Null when the Desktop is unknown, or
    // when the turma (it comes from a file) isn't one plain folder name and
    // would reach out of the Desktop.
    private string? DesktopFolder(ClassLesson lesson)
    {
        var name = lesson.FolderName;
        if (string.IsNullOrWhiteSpace(_desktopDirectory) || !string.Equals(FileNames.Safe(name), name, StringComparison.Ordinal))
        {
            return null;
        }
        return Path.Combine(_desktopDirectory, name);
    }

    private string? SetAsideFolder(Guid lessonId) =>
        string.IsNullOrWhiteSpace(_setAsideRoot) || lessonId == Guid.Empty
            ? null
            : Path.Combine(_setAsideRoot, lessonId.ToString("N"));

    // Where that lesson's files are now: the Desktop folder when it is the
    // current lesson, the set-aside folder when it isn't (and onlyCurrent is
    // off). Null with the reason when there is none, or it is a link.
    private (string? Folder, string Reason) FindFolder(ClassLesson? current, Guid lessonId, bool onlyCurrent)
    {
        string? folder;
        if (current is not null && current.LessonId == lessonId)
        {
            folder = DesktopFolder(current);
            if (folder is null || !Directory.Exists(folder))
            {
                return (null, NoFolder);
            }
        }
        else
        {
            folder = onlyCurrent ? null : SetAsideFolder(lessonId);
            if (folder is null || !Directory.Exists(folder))
            {
                return (null, OtherLesson);
            }
        }
        return IsLink(folder) ? (null, Linked) : (folder, string.Empty);
    }

    // The existing file at the untrusted relativePath under folder, or null.
    private static string? ExistingFile(string folder, string relativePath)
    {
        var path = Inside(folder, FileCollection.SafeRelativePath(relativePath));
        return path is not null && File.Exists(path) ? path : null;
    }

    // folder plus segments, or null when that leaves the folder or goes
    // through a link. A piece that doesn't exist (yet) can't be a link.
    private static string? Inside(string folder, IReadOnlyList<string> segments)
    {
        var path = folder;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            if (IsLink(path))
            {
                return null;
            }
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    // A junction or symbolic link (a reparse point). A path that isn't there
    // isn't one; one whose attributes can't be read is taken for one.
    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // The set-aside folders that are a lesson's (named by its id, "N" format),
    // in name order.
    private List<(Guid LessonId, DirectoryInfo Directory)> SetAsideFolders()
    {
        var folders = new List<(Guid, DirectoryInfo)>();
        if (string.IsNullOrWhiteSpace(_setAsideRoot) || !Directory.Exists(_setAsideRoot))
        {
            return folders;
        }

        try
        {
            foreach (var directory in new DirectoryInfo(_setAsideRoot).EnumerateDirectories("*", Options)
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (Guid.TryParseExact(directory.Name, "N", out var lessonId) && lessonId != Guid.Empty)
                {
                    folders.Add((lessonId, directory));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return folders;
    }

    // Manual recursion, as DeliveryFolder.ListFiles does, to stop at MaxDepth
    // (the folder itself is depth 1).
    private void Walk(DirectoryInfo directory, Guid lessonId, string prefix, int depth, List<ClassFolderEntry> entries)
    {
        List<FileInfo> files;
        List<DirectoryInfo> subdirectories;
        try
        {
            files = directory.EnumerateFiles("*", Options)
                .Where(IsListed)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            subdirectories = directory.EnumerateDirectories("*", Options)
                .Where(subdirectory => IsAddressable(subdirectory.Name))
                .OrderBy(subdirectory => subdirectory.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // gone or closed to the student while we looked: nothing from it this time
        }

        foreach (var file in files)
        {
            if (entries.Count >= _maxEntries)
            {
                return;
            }

            var path = prefix + file.Name;
            if (FitsTheWire(path))
            {
                entries.Add(new ClassFolderEntry(lessonId, path, file.Length, file.LastWriteTimeUtc.Ticks));
            }
        }

        if (depth >= FileCollection.MaxDepth)
        {
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            if (entries.Count >= _maxEntries)
            {
                return;
            }
            Walk(subdirectory, lessonId, prefix + subdirectory.Name + "/", depth + 1, entries);
        }
    }

    private static bool IsListed(FileInfo file)
    {
        var name = file.Name;
        return !name.StartsWith("~$", StringComparison.Ordinal)
            && !name.StartsWith(".midview-", StringComparison.OrdinalIgnoreCase)
            && !SkippedSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            && file.Length <= FileTransfer.MaxFileSize
            && IsAddressable(name);
    }

    // Every message names a file by a path run through
    // FileCollection.SafeRelativePath; a name it would rewrite (one over
    // FileNames.MaxNameLength, say) could never be found again.
    private static bool IsAddressable(string name) =>
        string.Equals(FileNames.Safe(name), name, StringComparison.Ordinal);

    // What ClassFolderWire.EncodeListing takes as a path.
    private static bool FitsTheWire(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && StrictUtf8.GetByteCount(path) <= FileTransfer.MaxNameBytes;
        }
        catch (EncoderFallbackException)
        {
            return false; // a name with half a surrogate pair, which NTFS allows
        }
    }
}

// A file being received into the class folder: written to a temp file next to
// its target and only moved there, with the time it had on the teacher's PC,
// when complete. Nothing not completed is left behind.
public sealed class ClassFolderIncoming : IDisposable
{
    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly FileStream _stream;
    private bool _completed;

    internal ClassFolderIncoming(string targetPath, string tempPath)
    {
        _targetPath = targetPath;
        _tempPath = tempPath;
        _stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public void Write(ReadOnlySpan<byte> data) => _stream.Write(data);

    // Replaces what is at the target, and gives it lastWriteTicks (UTC ticks).
    // An IOException (the old file held open by a program, say) propagates
    // and the temp file is deleted by Dispose, so the old file stays as it was.
    public void Complete(long lastWriteTicks)
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();

        // Before the move, so the file never shows up under its real name with
        // a time the Master didn't send (it would take that for the student's
        // own edit). The move is within one folder, and keeps the time.
        File.SetLastWriteTimeUtc(_tempPath, new DateTime(lastWriteTicks, DateTimeKind.Utc));
        try
        {
            File.Move(_tempPath, _targetPath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex) when (File.Exists(_targetPath))
        {
            // Windows answers "access denied", not "in use", when a program holds
            // the old file open without sharing it for replacing. As a sharing
            // violation, ErrorText words it for the teacher.
            throw new IOException($"{Path.GetFileName(_targetPath)} não pôde ser substituído: {ex.Message}",
                unchecked((int)0x80070020));
        }
        _completed = true;
    }

    // Anything not completed is thrown away.
    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        _stream.Dispose();
        try
        {
            File.Delete(_tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
