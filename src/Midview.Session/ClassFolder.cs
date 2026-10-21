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
//
// In two files: this one (the folders and what the teacher's Master asks of
// them) and ClassFolder.Follow.cs (following the lesson the Agent says is on,
// setting a finished lesson's files aside, and the state file); the file being
// received is ClassFolderIncoming.
public sealed partial class ClassFolder
{
    private const string OtherLesson = "aula diferente";
    private const string NoFolder = "sem pasta";
    private const string Linked = "pasta vinculada";

    // Hidden and system entries (Office's "~$" lock files, say) are skipped, and
    // so is a folder the student can't look into: a listing makes do with what it
    // can see. Links are skipped by Enumerate, ClassFolderRules saying what a link
    // is (a junction or symbolic link, no other kind of reparse point).
    private static readonly EnumerationOptions Options = ListingOptions(ignoreInaccessible: true);

    // The same, except that a folder that can't be looked into is an error
    // instead of an empty one: for Follow, which must not take "couldn't
    // read" for "nothing there".
    private static readonly EnumerationOptions StrictOptions = ListingOptions(ignoreInaccessible: false);

    private readonly string _desktopDirectory;
    private readonly string _setAsideRoot;
    private readonly Func<ClassLesson?> _agentLesson;
    private readonly IRecycleBin _recycleBin;
    private readonly int _maxEntries;

    // desktopDirectory: the student's Desktop ("" when unknown). setAsideRoot:
    // where a finished lesson's files wait, one folder per lesson id.
    // agentLesson: the lesson the Agent says is on (an ended lesson says it
    // has ended; null is "not known", such as a lesson file that couldn't be
    // read just now, and Follow does nothing with it). sessionStatePath: where
    // the lesson this Session last followed is remembered, with the finished
    // ones whose folder isn't settled yet.
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
    // the current lesson first. Left out: hidden or system attributes and links,
    // and what ClassFolderRules says (the same rules the Master's store applies):
    // lock files, Midview's own and still-being-written ones, anything deeper
    // than FileCollection.MaxDepth, and anything the wire format or the other
    // messages couldn't carry (a file over FileTransfer.MaxFileSize, a path
    // over FileTransfer.MaxNameBytes of UTF-8, a name that
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
            Walk(new DirectoryInfo(folder), lesson.LessonId, entries);
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
                Walk(directory, lessonId, entries);
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
        var tempPath = Path.Combine(directory, ClassFolderRules.NewTempName());
        return new ClassFolderIncoming(target, tempPath);
    }

    // Null when a file of that size fits on the Desktop's disk, otherwise why
    // not: in the words FileInbox uses for the same question. For the caller of
    // BeginPush to ask before it accepts what it can't keep.
    public string? RefusalFor(long size) => new FileInbox(_desktopDirectory).RefusalFor(size);

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

    // A junction or symbolic link (not any other reparse point: see
    // ClassFolderRules.IsLink). A path that isn't there isn't one; one whose
    // attributes can't be read is taken for one.
    private static bool IsLink(string path) => ClassFolderRules.IsLink(path);

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
                // A junction or symbolic link named like a lesson is not one: Options no longer
                // leaves out reparse points, and it would list what is outside.
                if (Guid.TryParseExact(directory.Name, "N", out var lessonId) && lessonId != Guid.Empty
                    && !ClassFolderRules.IsLink(directory))
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

    // What a listing shows of that folder: the files, each with its path from
    // the folder ('/' between the segments), files before subfolders and in name
    // order, nothing Options or ClassFolderRules leave out. One place for it,
    // because what Follow sets aside has to be what the Master was shown, and
    // no more.
    //
    // A folder that can't be read (gone, or closed to the student, while we
    // looked) gives nothing this time, and with no unreadable callback that is
    // all there is to it, as for a listing. With one, nothing is skipped quietly:
    // a folder the student can't look into is an error (StrictOptions) and the
    // callback is told which and why, so that the caller can tell "nothing
    // there" from "couldn't see".
    //
    // Manual recursion, as DeliveryFolder.ListFiles does, to stop at MaxDepth
    // (the folder itself is depth 1).
    private static IEnumerable<(FileInfo File, string Path)> ListedFiles(
        DirectoryInfo directory, string prefix, int depth, Action<DirectoryInfo, Exception>? unreadable = null)
    {
        var (files, subdirectories, failure) = Enumerate(directory, unreadable is null ? Options : StrictOptions);
        if (failure is not null)
        {
            unreadable?.Invoke(directory, failure);
            yield break;
        }

        foreach (var file in files)
        {
            var path = prefix + file.Name;
            if (ClassFolderRules.FitsTheWire(path))
            {
                yield return (file, path);
            }
        }

        if (!ClassFolderRules.CanDescendFrom(depth))
        {
            yield break;
        }

        foreach (var subdirectory in subdirectories)
        {
            foreach (var listed in ListedFiles(subdirectory, prefix + subdirectory.Name + "/", depth + 1, unreadable))
            {
                yield return listed;
            }
        }
    }

    private static (List<FileInfo> Files, List<DirectoryInfo> Subdirectories, Exception? Failure) Enumerate(
        DirectoryInfo directory, EnumerationOptions options)
    {
        try
        {
            var files = directory.EnumerateFiles("*", options)
                .Where(file => ClassFolderRules.IsListedFile(file.Name, file.Length) && !ClassFolderRules.IsLink(file))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var subdirectories = directory.EnumerateDirectories("*", options)
                .Where(subdirectory => ClassFolderRules.IsAddressable(subdirectory.Name)
                    && !ClassFolderRules.IsLink(subdirectory))
                .OrderBy(subdirectory => subdirectory.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return (files, subdirectories, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (new List<FileInfo>(), new List<DirectoryInfo>(), ex);
        }
    }

    private static EnumerationOptions ListingOptions(bool ignoreInaccessible) => new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = ignoreInaccessible,
    };

    private void Walk(DirectoryInfo directory, Guid lessonId, List<ClassFolderEntry> entries)
    {
        foreach (var (file, path) in ListedFiles(directory, string.Empty, depth: 1))
        {
            if (entries.Count >= _maxEntries)
            {
                return;
            }
            entries.Add(new ClassFolderEntry(lessonId, path, file.Length, file.LastWriteTimeUtc.Ticks));
        }
    }
}
