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
    // Office or editor ".tmp", a ".part" and Midview's ".parcial". The rule is
    // ClassFolderRules', shared with the Master's store.
    public static IReadOnlyList<string> SkippedSuffixes => ClassFolderRules.SkippedSuffixes;

    private const string OtherLesson = "aula diferente";
    private const string NoFolder = "sem pasta";
    private const string Linked = "pasta vinculada";

    // Hidden and system entries (Office's "~$" lock files, say) and links are
    // skipped, and so is a folder the student can't look into: a listing makes
    // do with what it can see.
    private static readonly EnumerationOptions Options = ListingOptions(ignoreInaccessible: true);

    // The same, except that a folder that can't be looked into is an error
    // instead of an empty one: for Follow, which must not take "couldn't
    // read" for "nothing there".
    private static readonly EnumerationOptions StrictOptions = ListingOptions(ignoreInaccessible: false);

    // Every entry, hidden, system and links included, an unreadable folder an
    // error: to find a link, which Options leaves out of sight.
    private static readonly EnumerationOptions AllEntries = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
    };

    private static readonly FollowedLesson NoLesson = new(Guid.Empty, string.Empty);

    private readonly object _followLock = new();
    private FollowState? _state; // read from SessionStatePath on the first Follow
    private readonly HashSet<string> _problems = new();

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

    // What the student is told when a lesson starts: shown once per lesson.
    public static string LessonNotice(ClassLesson lesson) =>
        $"Aula do {lesson.Turma}{Environment.NewLine}"
        + $"O que você baixar ou salvar na {lesson.FolderName} (na Área de Trabalho) aparece em todos os computadores da sala.";

    // Called every few seconds. Compares the lesson the Agent says is on with
    // the one this Session last followed (remembered in SessionStatePath, so a
    // restart doesn't repeat itself). A lesson that is on and new gets its
    // notice, if there is somewhere to show it, and is remembered at once.
    // The one it replaces, if there was one, is kept in a list of finished
    // lessons not yet settled (in the state file too), and every one in the
    // list is tried on every call:
    //  - the folder of the finished lesson, if it is still on the Desktop, has
    //    the files a listing shows (List's own filters) moved, relative paths
    //    kept, to setAsideRoot\<lesson id, "N" format>, where the teacher's
    //    Master collects what it hasn't got yet. Whatever else is left in it (a
    //    half-downloaded file, a lock file, a hidden one...) goes with the
    //    folder itself to the student's Recycle Bin, so what is set aside is
    //    always something the Master can see. The Agent releases the folder so
    //    the student can delete it; until it does the Recycle Bin is refused
    //    (access denied), and so is a file a program holds open, or a folder
    //    that can't be read: the lesson stays in the list for the next call,
    //    and holds nothing else back.
    //  - a settled lesson leaves the list.
    // An agentLesson of null is not known (the lesson file couldn't be read,
    // say), never "the lesson is over" (that is an ended lesson): nothing
    // happens then, so a failed read mid-lesson moves no file.
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
        var reported = _agentLesson();
        if (reported is null)
        {
            return;
        }

        var lesson = reported.IsOn ? reported : null;
        var now = lesson is null ? NoLesson : new FollowedLesson(lesson.LessonId, lesson.Turma);
        var state = _state ??= ReadState();
        var changed = false;
        try
        {
            if (now != state.Current)
            {
                // The new lesson is taken up at once, whatever state the old one's folder is in.
                if (state.Current.LessonId != Guid.Empty && !state.Pending.Contains(state.Current))
                {
                    state.Pending.Add(state.Current);
                }

                if (lesson is not null)
                {
                    ShowNotice(notices, lesson);
                }

                // In memory too, whatever happens to the file: it must not make the notice come back every few seconds.
                state.Current = now;
                changed = true;
            }

            for (var index = 0; index < state.Pending.Count;)
            {
                if (SetAsideSafely(state.Pending[index], lesson))
                {
                    state.Pending.RemoveAt(index);
                    changed = true;
                }
                else
                {
                    index++;
                }
            }

            if (state.Pending.Count == 0)
            {
                _problems.Clear();
            }
        }
        finally
        {
            if (changed)
            {
                SaveState(state);
            }
        }
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

    // One lesson's trouble is that lesson's: it is tried again, and the others go on.
    private bool SetAsideSafely(FollowedLesson finished, ClassLesson? current)
    {
        try
        {
            return SetAside(finished, current);
        }
        catch (Exception ex)
        {
            Problem($"Setting aside the files of {finished.Turma} failed: {ex.Message}");
            return false;
        }
    }

    // Moves the files of the finished lesson's Desktop folder that a listing
    // shows aside, then sends the folder, with whatever else is in it, to the
    // Recycle Bin. False when that has to be tried again.
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

        var moved = MoveListedFiles(new DirectoryInfo(folder), destination);
        RemoveEmptyFolders(destination);
        if (!moved)
        {
            return false;
        }

        // A folder with a link in it is not handed to the Recycle Bin: whether the shell would follow the link isn't ours to find out.
        var (links, detail) = SearchForLinks(folder);
        if (links == LinkSearch.Unreadable)
        {
            Problem($"Could not look through {folder} yet: {detail}");
            return false;
        }

        if (links == LinkSearch.Found)
        {
            Problem($"Leaving {folder}: there is a link in it, which isn't followed or removed.");
            return true;
        }

        try
        {
            _recycleBin.Recycle(folder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem($"The folder {folder} can't be removed yet: {ex.Message}");
            return false;
        }
    }

    // The files ListedFiles finds under source go to the same place under
    // destination (a name taken there is kept: the file coming gets another
    // one). False when one couldn't be moved, or a folder couldn't be read, so
    // that there may be more to move.
    private bool MoveListedFiles(DirectoryInfo source, string destination)
    {
        var moved = true;
        var listed = ListedFiles(source, string.Empty, depth: 1, (directory, failure) =>
        {
            moved = false;
            Problem($"Could not look into {directory.FullName}: {failure.Message}");
        }).ToList();

        foreach (var (file, path) in listed)
        {
            var target = Path.Combine(destination, path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file.FullName, FreeName(target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                moved = false;
                Problem($"Could not set {file.FullName} aside: {ex.Message}");
            }
        }
        return moved;
    }

    private enum LinkSearch
    {
        None,
        Found,
        Unreadable,
    }

    // Is there a link (junction or symbolic link) anywhere under folder, at
    // any depth? None is entered. A folder that can't be looked into is not
    // "no link" and not "a link" either: it is Unreadable, to be asked again.
    // Not recursive in code, because a student can nest folders as deep as
    // a path allows.
    private static (LinkSearch Result, string Detail) SearchForLinks(string folder)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(folder));
        try
        {
            while (pending.Count > 0)
            {
                foreach (var entry in pending.Pop().EnumerateFileSystemInfos("*", AllEntries))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return (LinkSearch.Found, string.Empty);
                    }

                    if (entry is DirectoryInfo directory)
                    {
                        pending.Push(directory);
                    }
                }
            }
            return (LinkSearch.None, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (LinkSearch.Unreadable, ex.Message);
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

    private FollowState ReadState()
    {
        try
        {
            if (!File.Exists(SessionStatePath))
            {
                return new FollowState(NoLesson, new List<FollowedLesson>());
            }

            using var stream = new FileStream(SessionStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var stored = JsonSerializer.Deserialize<StoredState>(stream);
            if (stored?.Turma is null)
            {
                return new FollowState(NoLesson, new List<FollowedLesson>());
            }

            // The file is the student's to edit: only what makes sense is taken.
            var pending = (stored.Pending ?? new List<FollowedLesson>())
                .Where(lesson => lesson?.Turma is not null && lesson.LessonId != Guid.Empty)
                .Distinct()
                .ToList();
            return new FollowState(new FollowedLesson(stored.LessonId, stored.Turma), pending);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not read {SessionStatePath}: {ex.Message}");
            return new FollowState(NoLesson, new List<FollowedLesson>());
        }
    }

    // Written to a temporary file and moved over the old one, as ClassLessonFile does.
    private void SaveState(FollowState state)
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
                JsonSerializer.Serialize(stream, new StoredState(state.Current.LessonId, state.Current.Turma, state.Pending));
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

    // What this Session follows: the lesson that is on (or none), and the
    // finished ones whose folder still has to be set aside, oldest first.
    private sealed class FollowState
    {
        public FollowState(FollowedLesson current, List<FollowedLesson> pending)
        {
            Current = current;
            Pending = pending;
        }

        public FollowedLesson Current { get; set; }

        public List<FollowedLesson> Pending { get; }
    }

    private sealed record StoredState(Guid LessonId, string Turma, List<FollowedLesson>? Pending = null);
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
                .Where(file => ClassFolderRules.IsListedFile(file.Name, file.Length))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var subdirectories = directory.EnumerateDirectories("*", options)
                .Where(subdirectory => ClassFolderRules.IsAddressable(subdirectory.Name))
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
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
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
