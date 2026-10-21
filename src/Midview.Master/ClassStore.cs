using System.IO;
using Midview.Protocol;
using static Midview.Master.ClassFolderDisk;

namespace Midview.Master;

// What the sync compares: a file's size and last write time, in UTC ticks
// (File.GetLastWriteTimeUtc(...).Ticks; File.SetLastWriteTimeUtc when a
// file is committed).
public readonly record struct FileVersion(long Size, long LastWriteTicks);

// One class in the "Pasta da turma" window: how many files it holds, their
// size, and the day the newest of them arrived (null when there are none).
public sealed record ClassSummary(string Turma, int Files, long Bytes, DateOnly? LastLesson);

// What the teacher chooses at the end of a lesson.
public enum EndChoice
{
    // Every file stays; it comes back to the PCs at the next lesson of the class.
    Keep,

    // The files that arrived today go (by the index's arrival date, not the file's own date).
    DeleteToday,

    // Every file of the class goes.
    DeleteAll,
}

// The teacher's PC's copy of each class's files, the copy that counts
// ("Pasta da turma", docs/superpowers/specs/2026-10-20-class-folder-design.md):
// one folder per class under filesRoot (Documents\Midview\Turmas), and one
// JSON index per class under indexRoot (MasterData.Folder\turmas), kept
// apart so the teacher browsing or deleting in Explorer can't corrupt it.
//
// The sync calls this from several PCs' rounds at once, so everything that
// reads or changes an index goes through one lock. Every change to an index
// is saved at once (ClassIndexFile), except the per-PC records, which wait
// for SaveIndex (the sync calls it after each PC's round).
//
// A class name is only ever one Create made: FileNames.Safe(name) == name,
// not empty, at most ClassLesson.MaxTurmaLength characters, no "${" (Chrome
// would expand it in the download folder). Every method that takes one
// throws ArgumentException for anything else, so a name can't climb out of
// filesRoot. Paths inside a class are '/'-separated, compare ignoring case,
// and each of their segments must be a name FileNames.Safe leaves alone.
//
// In four files, to stay readable: this one (the classes and their files),
// ClassStore.Incoming.cs (what the index knows, receiving a file, the teacher's
// changes in Explorer, arrival days), ClassStore.Lesson.cs (ending a lesson) and
// ClassStore.LessonMap.cs (which class each lesson was of).
public sealed partial class ClassStore
{
    // Under this much free space the Master warns the teacher.
    public const long LowSpaceBytes = 5L * 1024 * 1024 * 1024;

    private const string NameTakenText = "Já existe uma turma com esse nome.";
    private const string NotFoundText = "Essa turma não existe mais.";

    // Windows can't store a time before 1601 (and a time of exactly 0 means
    // "leave as is" to SetFileTime).
    private static readonly long FileTimeEpochTicks = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly string _filesRoot;
    private readonly string _indexRoot;
    private readonly IRecycleBin _recycleBin;
    private readonly Func<DateTime> _localNow;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, ClassIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);

    // Copies of replaced files that were set aside (CommitIncoming) and that the Recycle Bin
    // hasn't taken yet: tried again at the next replace and at the end of a lesson.
    private readonly List<string> _asides = new();

    // log gets what went wrong that the caller isn't told, in English (for a developer):
    // by default it goes to System.Diagnostics.Trace. lessonsPath: see LessonsMap.
    public ClassStore(
        string filesRoot, string indexRoot, IRecycleBin recycleBin, Func<DateTime> localNow, Action<string>? log = null,
        string? lessonsPath = null)
    {
        _filesRoot = Path.GetFullPath(filesRoot);
        _indexRoot = Path.GetFullPath(indexRoot);
        _recycleBin = recycleBin;
        _localNow = localNow;
        _log = log ?? (message => System.Diagnostics.Trace.WriteLine("ClassStore: " + message));
        _lessons = LessonsMap(_indexRoot, lessonsPath, _log);
    }

    public static string DefaultFilesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Midview", "Turmas");

    public static string DefaultIndexRoot => Path.Combine(MasterData.Folder, "turmas");

    // "Today" for arrival dates is the teacher's own local date.
    private DateOnly Today => DateOnly.FromDateTime(_localNow());

    // What is left on the disk that holds the classes; long.MaxValue when that
    // can't be told (a network folder), so that nothing warns.
    public long FreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(_filesRoot);
            return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    // --- Classes ---

    // Whether the name is one a class can have (see the class comment).
    public static bool IsValidName(string name) => NameRefusal(name) is null;

    // Whether there is a folder for that class (a link doesn't count).
    public bool Exists(string turma) => IsValidName(turma) && IsRealDirectory(Path.Combine(_filesRoot, turma));

    // The classes, in natural order ("3º A", "3º B", "10º A"). Folders that
    // aren't a usable class (a name over the limit, a link) are left out:
    // a lesson couldn't be started with them.
    public IReadOnlyList<ClassSummary> List()
    {
        var names = new List<string>();
        if (Directory.Exists(_filesRoot))
        {
            foreach (var directory in new DirectoryInfo(_filesRoot).EnumerateDirectories("*", ListedDirectories))
            {
                if (IsValidName(directory.Name) && !ClassFolderRules.IsLink(directory))
                {
                    names.Add(directory.Name);
                }
            }
        }
        names.Sort(NaturalOrder.Instance);

        var summaries = new List<ClassSummary>();
        foreach (var name in names)
        {
            // What can be read of it: a summary for the window, not for the sync.
            var files = Scan(new DirectoryInfo(FolderOf(name)), strict: false);
            summaries.Add(new ClassSummary(
                name, files.Count, files.Values.Sum(version => version.Size), LastArrival(name, files)));
        }
        return summaries;
    }

    // Detail is why not (in the teacher's words) and Turma empty when it
    // isn't Ok; on success Turma is the name the folder got, which can
    // differ from the one asked for ("Turma: 3º A" is "Turma_ 3º A", and
    // "Turma 1/2" is "Turma 1-2": a slash is a hyphen, so none of the name is lost).
    public (bool Ok, string Detail, string Turma) Create(string requestedName)
    {
        var (name, refusal) = Normalize(requestedName);
        if (refusal is not null)
        {
            return (false, refusal, string.Empty);
        }

        lock (_gate)
        {
            if (NameTaken(name, except: null))
            {
                return (false, NameTakenText, string.Empty);
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(_filesRoot, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, "Não foi possível criar a turma: " + ErrorText.Describe(ex), string.Empty);
            }

            // Whatever an earlier class of that name left is not this one's.
            DiscardIndex(name);
            return (true, string.Empty, name);
        }
    }

    // Moves the class's folder and index to the new name (made safe as
    // Create does). Changing only the case of the name is fine. Whether the
    // class has a lesson on is for the caller to say.
    public (bool Ok, string Detail) Rename(string turma, string newName)
    {
        var folder = FolderOf(turma);
        var (name, refusal) = Normalize(newName);

        lock (_gate)
        {
            if (!IsRealDirectory(folder))
            {
                return (false, NotFoundText);
            }
            if (refusal is not null)
            {
                return (false, refusal);
            }
            if (string.Equals(name, turma, StringComparison.Ordinal))
            {
                return (true, string.Empty);
            }
            if (NameTaken(name, except: turma))
            {
                return (false, NameTakenText);
            }

            var caseOnly = string.Equals(name, turma, StringComparison.OrdinalIgnoreCase);
            try
            {
                MoveFolder(folder, FolderOf(name), caseOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, "Não foi possível renomear a turma: " + ErrorText.Describe(ex));
            }

            _lessons.RenameClass(turma, name);

            // The folder has moved; the index follows it. Not worth undoing the
            // rename if that fails: the index only holds what Midview knows
            // about the files, and what is in memory is saved with the next change.
            try
            {
                var index = IndexOf(turma);
                _indexes.Remove(turma);
                _indexes[name] = index;
                Save(name, index);
                if (!caseOnly)
                {
                    File.Delete(IndexPath(turma));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The rename stands.
            }
            return (true, string.Empty);
        }
    }

    // The class's folder goes to the Recycle Bin, so it can be undone; its
    // index is deleted.
    public (bool Ok, string Detail) Delete(string turma)
    {
        var folder = FolderOf(turma);
        lock (_gate)
        {
            if (!IsRealDirectory(folder))
            {
                return (false, NotFoundText);
            }

            try
            {
                _recycleBin.Recycle(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, "Não foi possível excluir a turma: " + ErrorText.Describe(ex));
            }

            DiscardIndex(turma);
            return (true, string.Empty);
        }
    }

    // --- The files ---

    // The class's files by their path ('/'-separated, case-insensitive), with
    // their version. Left out: what the Session's listing leaves out, which is
    // hidden and system files, links, and what ClassFolderRules says (lock,
    // in-progress and Midview's own files, a file over FileTransfer.MaxFileSize,
    // a name FileNames.Safe would rewrite, a path over FileTransfer.MaxNameBytes
    // of UTF-8, anything deeper than FileCollection.MaxDepth path segments, the
    // file name counted), so the sync never pushes a path the student's side
    // would rewrite or refuse, every round. Empty for a class whose folder
    // isn't there.
    //
    // Never a partial listing: a folder inside that can't be read throws
    // IOException (naming it), for the caller to skip the round, instead of
    // leaving out what is in it, which the sync would take for deleted or new.
    public IReadOnlyDictionary<string, FileVersion> Files(string turma)
    {
        var folder = FolderOf(turma);
        return IsRealDirectory(folder)
            ? Scan(new DirectoryInfo(folder), strict: true)
            : new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
    }

    // Where a file of the class is (or would be): its full path. Throws
    // ArgumentException for a path that doesn't stay inside the class
    // folder: '..', a drive, a device name, a stream, an empty or too odd
    // segment, or a link on the way.
    public string PathOf(string turma, string relativePath) =>
        PathInside(FolderOf(turma), relativePath)
            ?? throw new ArgumentException($"Not a path inside the class folder: {relativePath}", nameof(relativePath));

    // --- Names and folders ---

    private static readonly EnumerationOptions ListedDirectories = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    // The name a request asks for, made safe, and why it can't be used (null if it
    // can). A slash or backslash becomes a hyphen first: FileNames.Safe would keep
    // only what follows the last one ("Turma 1/2" would be "2").
    private static (string Name, string? Refusal) Normalize(string? requested)
    {
        var trimmed = (requested ?? string.Empty).Trim();
        var name = trimmed.Length == 0 ? trimmed : FileNames.Safe(trimmed.Replace('/', '-').Replace('\\', '-'));
        return (name, NameRefusal(name));
    }

    private static string? NameRefusal(string name)
    {
        if (name.Length == 0)
        {
            return "Escolha um nome para a turma.";
        }
        if (name.Length > ClassLesson.MaxTurmaLength)
        {
            return $"Nome longo demais: use até {ClassLesson.MaxTurmaLength} letras.";
        }
        if (name.Contains("${", StringComparison.Ordinal))
        {
            return "O nome da turma não pode ter \"${\".";
        }
        if (!ClassFolderRules.IsAddressable(name))
        {
            return "Esse nome não pode ser usado como nome de pasta.";
        }
        return null;
    }

    // Whether a class, or anything else in the folder, already has the name,
    // ignoring case; except is the class being renamed.
    private bool NameTaken(string name, string? except)
    {
        if (except is not null && string.Equals(except, name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return Directory.Exists(_filesRoot)
            && Directory.EnumerateFileSystemEntries(_filesRoot)
                .Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));
    }

    // Directory.Move refuses a move onto itself, which is what a change of
    // case alone looks like: that goes by way of a temporary name.
    private static void MoveFolder(string from, string to, bool caseOnly)
    {
        if (!caseOnly)
        {
            Directory.Move(from, to);
            return;
        }

        var stepping = from + ".renomeando-" + Guid.NewGuid().ToString("N");
        Directory.Move(from, stepping);
        try
        {
            Directory.Move(stepping, to);
        }
        catch
        {
            Directory.Move(stepping, from);
            throw;
        }
    }

    // The folder of a class that is a valid name, whether or not it is there.
    private string FolderOf(string turma)
    {
        if (!IsValidName(turma))
        {
            throw new ArgumentException($"Not a class name: {turma}", nameof(turma));
        }
        return Path.Combine(_filesRoot, turma);
    }

    private string RealFolderOf(string turma)
    {
        var folder = FolderOf(turma);
        if (!IsRealDirectory(folder))
        {
            throw new DirectoryNotFoundException($"A pasta da turma {turma} não existe.");
        }
        return folder;
    }

    // --- The index ---

    private string IndexPath(string turma) => Path.Combine(_indexRoot, turma + ".json");

    // With _gate held. Loaded once, then kept: every change goes through here.
    private ClassIndex IndexOf(string turma)
    {
        FolderOf(turma); // refuses a name that is no class name before it reaches a path
        if (!_indexes.TryGetValue(turma, out var index))
        {
            index = ClassIndexFile.Load(IndexPath(turma), out _);
            _indexes[turma] = index;
        }
        return index;
    }

    // With _gate held. Writes all of the index, so whatever was pending goes with it.
    // It stays pending (Dirty) if the write fails, for SaveIndex to try again.
    private void Save(string turma, ClassIndex index)
    {
        index.Dirty = true;
        ClassIndexFile.Save(IndexPath(turma), index);
        index.Dirty = false;
    }

    // For after something was done on the disk: a failed save is no reason to fail what is
    // done (the caller would do it again), so the index stays pending and the failure is logged.
    private void TrySave(string turma, ClassIndex index)
    {
        try
        {
            Save(turma, index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The index of {turma} could not be saved yet: {ex.Message}");
        }
    }

    // With _gate held. The class's lessons go too: a class made later under the name is another.
    private void DiscardIndex(string turma)
    {
        _lessons.ForgetClass(turma);
        _indexes.Remove(turma);
        try
        {
            File.Delete(IndexPath(turma));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It only holds what Midview knew about the files of a class that is gone.
        }
    }
}
