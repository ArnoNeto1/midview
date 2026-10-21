using System.Text.Json;
using Midview.Protocol;

namespace Midview.Session;

// ClassFolder: following the lesson the Agent says is on -- the notice for a
// new lesson, and a finished lesson's files set aside -- and the state file
// that remembers what this Session last followed.
public sealed partial class ClassFolder
{
    // Every entry, hidden and system ones included, an unreadable folder an
    // error: to find a link, which a listing leaves out of sight.
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
                    if (ClassFolderRules.IsLink(entry))
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
}
