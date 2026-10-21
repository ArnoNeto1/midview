using System.Text;
using System.Text.Json;
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

    // Moving a finished lesson's folder aside takes everything in it, hidden
    // or not (it has to go empty); only links are left out, and not followed.
    private static readonly EnumerationOptions EverythingButLinks = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = true,
    };

    private static readonly FollowedLesson NoLesson = new(Guid.Empty, string.Empty);

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private readonly object _followLock = new();
    private FollowedLesson? _followed; // read from SessionStatePath on the first Follow
    private readonly HashSet<string> _problems = new();

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

    // What the student is told when a lesson starts: shown once per lesson.
    public static string LessonNotice(ClassLesson lesson) =>
        $"Aula do {lesson.Turma}{Environment.NewLine}"
        + $"O que você baixar ou salvar na {lesson.FolderName} (na Área de Trabalho) aparece em todos os computadores da sala.";

    // Called every few seconds. Compares the lesson the Agent says is on with
    // the one this Session last followed (remembered in SessionStatePath, so a
    // restart doesn't repeat itself) and when they differ:
    //  1. the folder of the lesson that is over, if it is still on the Desktop,
    //     has everything in it moved (relative paths kept) to
    //     setAsideRoot\<lesson id, "N" format>, where the teacher's Master
    //     collects what it hasn't got yet, and is then removed. The Agent
    //     releases it so the student can delete it; until it does the removal
    //     is refused (access denied), and so is a file a program holds open:
    //     what is left is tried again on the next call, and the old lesson is
    //     still the one remembered.
    //  2. a lesson that is on gets its notice, if there is somewhere to show it.
    //  3. the new lesson is remembered.
    // Never throws.
    public void Follow(IMessageDisplay? notices)
    {
        if (!Monitor.TryEnter(_followLock))
        {
            return; // the last call is still moving files; it will see the same
        }

        try
        {
            FollowLesson(notices);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Following the class lesson failed: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_followLock);
        }
    }

    private void FollowLesson(IMessageDisplay? notices)
    {
        var lesson = CurrentLesson();
        var now = lesson is null ? NoLesson : new FollowedLesson(lesson.LessonId, lesson.Turma);
        var before = _followed ??= ReadFollowed();
        if (now == before)
        {
            return;
        }

        if (before.LessonId != Guid.Empty && !SetAside(before, lesson))
        {
            return;
        }

        _problems.Clear();
        if (lesson is not null)
        {
            ShowNotice(notices, lesson);
        }

        // In memory first: a state file that can't be written must not make the notice come back every few seconds.
        _followed = now;
        SaveFollowed(now);
    }

    private static void ShowNotice(IMessageDisplay? notices, ClassLesson lesson)
    {
        if (notices is null)
        {
            return;
        }

        try
        {
            notices.Show(LessonNotice(lesson));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Showing the class lesson notice failed: {ex.Message}");
        }
    }

    // Moves what the student has in the finished lesson's Desktop folder aside
    // and removes the folder. False when that has to be tried again.
    private bool SetAside(FollowedLesson finished, ClassLesson? current)
    {
        // The turma comes from the student's own state file: it goes through the same check as the Agent's.
        var folder = DesktopFolder(new ClassLesson(0, finished.LessonId, finished.Turma, 0, false));
        if (folder is null || !Directory.Exists(folder))
        {
            return true; // no Desktop, or the folder is gone already
        }

        // The next lesson is of the same turma: the folder is the new lesson's now, with the same class's files.
        if (current is not null && string.Equals(DesktopFolder(current), folder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var destination = SetAsideFolder(finished.LessonId);
        if (destination is null || IsLink(folder) || IsLink(destination))
        {
            // Nowhere to put the files, or a link in the way: the student keeps them where they are.
            Problem($"Leaving {folder} as it is: the files can't be set aside.");
            return true;
        }

        var left = new Leftovers();
        MoveAll(new DirectoryInfo(folder), destination, left);
        RemoveEmptyFolders(destination);
        if (left.Failed)
        {
            return false;
        }

        if (left.Linked)
        {
            Problem($"Leaving {folder}: there is a link in it, which isn't followed or removed.");
            return true;
        }

        try
        {
            Directory.Delete(folder); // not recursive: only an empty folder goes
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem($"The folder {folder} can't be removed yet: {ex.Message}");
            return false;
        }
    }

    // Everything under source goes to the same place under destination, a link
    // is never followed (nor moved), and a name taken there is kept as it is:
    // the file coming gets another one. The folders left empty go.
    private void MoveAll(DirectoryInfo source, string destination, Leftovers left)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = source.EnumerateFileSystemInfos("*", EverythingButLinks).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            left.Failed = true;
            Problem($"Could not look into {source.FullName}: {ex.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                left.Linked = true;
                continue;
            }

            var target = Path.Combine(destination, entry.Name);
            try
            {
                if (entry is DirectoryInfo directory)
                {
                    MoveAll(directory, target, left);
                    TryDeleteFolder(directory.FullName);
                }
                else
                {
                    Directory.CreateDirectory(destination);
                    File.Move(entry.FullName, FreeName(target));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left.Failed = true;
                Problem($"Could not set {entry.FullName} aside: {ex.Message}");
            }
        }
    }

    // Empty by now, or it stays; then the folder above can't go either, which is what says "try again".
    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // path, or the first of "name (2).ext", "name (3).ext"... that is free.
    private static string FreeName(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 2; ; number++)
        {
            var candidate = Path.Combine(directory, $"{name} ({number}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // Folders with nothing in them, the folder itself included. Never recursive
    // on a folder that has something in it, and never into a link.
    private static void RemoveEmptyFolders(string folder)
    {
        try
        {
            if (!Directory.Exists(folder) || IsLink(folder))
            {
                return;
            }

            foreach (var subfolder in Directory.EnumerateDirectories(folder))
            {
                RemoveEmptyFolders(subfolder);
            }
            Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not empty, or not ours to remove: it stays
        }
    }

    // Said once, not every few seconds for as long as it lasts.
    private void Problem(string text)
    {
        if (_problems.Add(text))
        {
            Console.WriteLine(text);
        }
    }

    private FollowedLesson ReadFollowed()
    {
        try
        {
            if (!File.Exists(SessionStatePath))
            {
                return NoLesson;
            }

            using var stream = new FileStream(SessionStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var stored = JsonSerializer.Deserialize<StoredFollowed>(stream);
            return stored?.Turma is null ? NoLesson : new FollowedLesson(stored.LessonId, stored.Turma);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not read {SessionStatePath}: {ex.Message}");
            return NoLesson;
        }
    }

    // Written to a temporary file and moved over the old one, as ClassLessonFile does.
    private void SaveFollowed(FollowedLesson lesson)
    {
        var tempPath = SessionStatePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(SessionStatePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new StoredFollowed(lesson.LessonId, lesson.Turma));
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, SessionStatePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Console.WriteLine($"Could not remember the class lesson in {SessionStatePath}: {ex.Message}");
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    private sealed record FollowedLesson(Guid LessonId, string Turma);

    private sealed record StoredFollowed(Guid LessonId, string Turma);

    // What MoveAll couldn't empty: a file it couldn't move (to be tried again),
    // a link (never to be).
    private sealed class Leftovers
    {
        public bool Failed { get; set; }

        public bool Linked { get; set; }
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
