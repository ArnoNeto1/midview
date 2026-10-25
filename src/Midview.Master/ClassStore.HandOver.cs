using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// One file of the store as a hand-over sends it (ClassFolderHandOver): its path
// on the wire ("files/<turma>/<path>", "index/<turma>.json" or "lessons.json"),
// where it is on this PC, and its size and last write time (UTC ticks).
internal sealed record HandOverFile(string WirePath, string FullPath, long Size, long LastWriteTicks);

// Where a hand-over puts the store it receives until all of it is there and
// checks out: beside the files root, the index root and the lessons map, each
// on that one's disk, so putting it in place is a rename.
internal sealed record HandOverStaging(string Files, string Index, string Lessons);

// What was put back after a swap (ClassStore.SwapIn) that didn't stand: All,
// or, stopped at the first thing that couldn't move back (a file of it open in
// another program), whether the received copy stayed in the files root
// (CopyStayed) and where this PC's own classes still are (OwnAside: the files
// root's aside, null when they aren't aside).
internal sealed record PutBack(bool All, bool CopyStayed, string? OwnAside);

// What ClassStore.SwapIn did, for when the room's PCs don't take this PC as the home after all.
internal interface IHandOverSwap
{
    // Where this PC's own earlier store went (its files root's new name); null when it had none.
    string? OwnStoreAside { get; }

    // Puts the received store back where it was received and this PC's own
    // back in place, the files root first, up to the first thing that can't
    // move back: what didn't move stays whole where it is.
    PutBack Undo();
}

// SwapIn couldn't put the received store in place, or MoveAside this PC's
// store aside (InnerException says why), and put back what it could
// (PutBack; for MoveAside, OwnAside is where its files root still is).
internal sealed class StoreMoveException : IOException
{
    public StoreMoveException(Exception inner, PutBack putBack)
        : base(inner.Message, inner)
    {
        PutBack = putBack;
    }

    public PutBack PutBack { get; }
}

// ClassStore: the whole store passing to another teacher PC ("Guardar as pastas
// das turmas neste computador", plan decision 7, Ruling 4). The PC that keeps
// the folders reads its whole store out (HandOverContents) and, once the other
// PC has it, moves it aside (MoveAside); the PC taking them over receives into
// Staging and swaps it in (SwapIn), its own earlier store going aside. Aside is
// "<name> (<why>)" beside each of the files root, the index root and the
// lessons map ("Turmas (passadas para PROF-2)", "turmas (passadas para
// PROF-2)", "class-lessons (passadas para PROF-2).json"), numbered " (2)"...
// when one of the three is taken; a folder with nothing in it is removed
// instead. What the store held in memory is forgotten after each of these:
// the next use reads the disk.
public sealed partial class ClassStore
{
    private const string StagingSuffix = " (recebendo)";

    // Names tried for an aside before giving up.
    private const int MaxAsideNumber = 1000;

    // Runs before each rename of a hand-over (from, to), forward or back: a
    // test's stand-in for a folder that can't move (it throws IOException).
    internal Action<string, string>? MoveHook { get; set; }

    // Where a hand-over receives (HandOverStaging).
    internal HandOverStaging Staging => new(
        FilesRootPath + StagingSuffix, IndexRootPath + StagingSuffix, _lessons.Path + ".recebendo");

    // The classes as ClassNames has them, then every file of each as Files lists
    // it (what the PCs get), then each class's index (records not saved yet are
    // saved first) and the lessons map, the ones that are there. Throws
    // IOException or UnauthorizedAccessException when a class's folder can't be
    // read whole, or an index can't be saved.
    internal (IReadOnlyList<string> Classes, IReadOnlyList<HandOverFile> Files) HandOverContents()
    {
        var classes = ClassNames();
        var files = new List<HandOverFile>();
        foreach (var turma in classes)
        {
            foreach (var (path, version) in Files(turma).OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                files.Add(new HandOverFile(
                    ClassFolderHandOver.FileWirePath(turma, path), PathOf(turma, path), version.Size, version.LastWriteTicks));
            }
        }

        lock (_gate)
        {
            foreach (var turma in classes)
            {
                if (_indexes.TryGetValue(turma, out var index) && index.Dirty)
                {
                    Save(turma, index);
                }
                if (DataFile(ClassFolderHandOver.IndexWirePath(turma), IndexPath(turma)) is { } indexFile)
                {
                    files.Add(indexFile);
                }
            }
        }
        if (DataFile(ClassFolderHandOver.LessonsWirePath, _lessons.Path) is { } lessons)
        {
            files.Add(lessons);
        }
        return (classes, files);
    }

    // What an earlier hand-over left half received, if anything. Never throws: what can't be removed is logged.
    internal void DiscardStaging()
    {
        var staging = Staging;
        foreach (var folder in new[] { staging.Files, staging.Index })
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    // Recursive deletion removes a link on the way, never what it points to.
                    Directory.Delete(folder, recursive: !ClassFolderRules.IsLink(folder));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"A half received copy of the class folders could not be removed from {folder}: {ex.Message}");
            }
        }
        try
        {
            File.Delete(staging.Lessons);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"A half received lessons map could not be removed: {ex.Message}");
        }
    }

    // What of Staging is still there (after DiscardStaging couldn't remove it:
    // a file of it open in another program, say), or null when none of it is.
    internal string? StagingLeft()
    {
        var staging = Staging;
        return Directory.Exists(staging.Files) ? staging.Files
            : Directory.Exists(staging.Index) ? staging.Index
            : File.Exists(staging.Lessons) ? staging.Lessons
            : null;
    }

    // This PC's store, all of it, aside (see the class comment), for the PC
    // that keeps the folders now: the store is empty after. The files root's
    // new name, or null when it had nothing. Throws StoreMoveException when it
    // can't, having put back what it could.
    internal string? MoveAside(string why)
    {
        lock (_gate)
        {
            var moves = new Moves(_log, MoveHook);
            try
            {
                return PutAside(moves, why);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var all = moves.Undo();
                throw new StoreMoveException(ex, new PutBack(all, false, all ? null : moves.DestinationOf(FilesRootPath)));
            }
            finally
            {
                ForgetAll();
            }
        }
    }

    // The store received into Staging, in place of this PC's own, which goes
    // aside; the files root goes in last, so it is the first to go back.
    // Throws StoreMoveException when it can't, having put back what it could
    // (the received store in Staging, this PC's own in place).
    internal IHandOverSwap SwapIn(string why)
    {
        var staging = Staging;
        lock (_gate)
        {
            var moves = new Moves(_log, MoveHook);
            try
            {
                var aside = PutAside(moves, why);
                if (Directory.Exists(staging.Index))
                {
                    moves.Folder(staging.Index, IndexRootPath);
                }
                if (File.Exists(staging.Lessons))
                {
                    moves.File(staging.Lessons, _lessons.Path);
                }
                moves.Folder(staging.Files, FilesRootPath);
                return new Swap(this, moves, aside, staging.Files);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new StoreMoveException(ex, UndoSwap(moves, staging.Files));
            }
            finally
            {
                ForgetAll();
            }
        }
    }

    private string FilesRootPath => Path.TrimEndingDirectorySeparator(_filesRoot);

    private string IndexRootPath => Path.TrimEndingDirectorySeparator(_indexRoot);

    // With _gate held. The files root, the index root and the lessons map, each
    // that has anything, to "<name> (<why>)" under one number; an empty folder
    // is removed. The files root's new name, or null when it had nothing.
    private string? PutAside(Moves moves, string why)
    {
        var number = FreeAsideNumber(why);
        string? aside = null;
        if (HasEntries(FilesRootPath))
        {
            aside = Beside(FilesRootPath, why, number, isFile: false);
            moves.Folder(FilesRootPath, aside);
        }
        else
        {
            moves.RemoveEmpty(FilesRootPath);
        }

        if (HasEntries(IndexRootPath))
        {
            moves.Folder(IndexRootPath, Beside(IndexRootPath, why, number, isFile: false));
        }
        else
        {
            moves.RemoveEmpty(IndexRootPath);
        }

        if (File.Exists(_lessons.Path))
        {
            moves.File(_lessons.Path, Beside(_lessons.Path, why, number, isFile: true));
        }
        return aside;
    }

    // The first number under which none of the three asides is taken (1: no number).
    private int FreeAsideNumber(string why)
    {
        for (var number = 1; number <= MaxAsideNumber; number++)
        {
            if (!Path.Exists(Beside(FilesRootPath, why, number, isFile: false))
                && !Path.Exists(Beside(IndexRootPath, why, number, isFile: false))
                && !Path.Exists(Beside(_lessons.Path, why, number, isFile: true)))
            {
                return number;
            }
        }
        throw new IOException($"Não há um nome livre para guardar as pastas à parte ({why}).");
    }

    // "Turmas" -> "Turmas (why)" or "Turmas (why) (2)"; "class-lessons.json" -> "class-lessons (why).json".
    private static string Beside(string path, string why, int number, bool isFile)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = isFile ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
        var extension = isFile ? Path.GetExtension(path) : string.Empty;
        var numbered = number == 1 ? string.Empty : $" ({number})";
        return Path.Combine(directory, $"{name} ({why}){numbered}{extension}");
    }

    private static bool HasEntries(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();

    private static HandOverFile? DataFile(string wirePath, string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists ? new HandOverFile(wirePath, fullPath, info.Length, info.LastWriteTimeUtc.Ticks) : null;
    }

    // With _gate held. What a swap's undo put back (see PutBack).
    private PutBack UndoSwap(Moves moves, string stagingFiles) =>
        moves.Undo()
            ? new PutBack(true, false, null)
            : new PutBack(false, moves.Holds(stagingFiles), moves.DestinationOf(FilesRootPath));

    // With _gate held: the folders and the map on disk are other ones now.
    private void ForgetAll()
    {
        _indexes.Clear();
        _asides.Clear();
        _lessons.Reload();
    }

    private sealed class Swap : IHandOverSwap
    {
        private readonly ClassStore _store;
        private readonly Moves _moves;
        private readonly string _stagingFiles;

        public Swap(ClassStore store, Moves moves, string? ownStoreAside, string stagingFiles)
        {
            _store = store;
            _moves = moves;
            _stagingFiles = stagingFiles;
            OwnStoreAside = ownStoreAside;
        }

        public string? OwnStoreAside { get; }

        public PutBack Undo()
        {
            lock (_store._gate)
            {
                try
                {
                    return _store.UndoSwap(_moves, _stagingFiles);
                }
                finally
                {
                    _store.ForgetAll();
                }
            }
        }
    }

    // The renames (and empty folders removed) done so far, undone in reverse,
    // up to the first that can't be (Undo says whether all were).
    private sealed class Moves
    {
        private readonly Action<string> _log;
        private readonly Action<string, string>? _hook;
        private readonly Stack<(string From, string To, Kind Kind)> _done = new();

        public Moves(Action<string> log, Action<string, string>? hook)
        {
            _log = log;
            _hook = hook;
        }

        private enum Kind
        {
            Folder,
            File,
            Removed,
        }

        public void Folder(string from, string to)
        {
            _hook?.Invoke(from, to);
            Directory.Move(from, to);
            _done.Push((from, to, Kind.Folder));
        }

        public void File(string from, string to)
        {
            _hook?.Invoke(from, to);
            System.IO.File.Move(from, to);
            _done.Push((from, to, Kind.File));
        }

        public void RemoveEmpty(string folder)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder); // not recursive: only ever an empty one
                _done.Push((folder, folder, Kind.Removed));
            }
        }

        public bool Undo()
        {
            while (_done.TryPeek(out var move))
            {
                try
                {
                    switch (move.Kind)
                    {
                        case Kind.Folder:
                            _hook?.Invoke(move.To, move.From);
                            Directory.Move(move.To, move.From);
                            break;
                        case Kind.File:
                            _hook?.Invoke(move.To, move.From);
                            System.IO.File.Move(move.To, move.From);
                            break;
                        default:
                            Directory.CreateDirectory(move.From);
                            break;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log($"{move.To} could not be put back as {move.From}: {ex.Message}");
                    return false;
                }
                _done.Pop();
            }
            return true;
        }

        // Whether a rename from there is still done (not undone).
        public bool Holds(string from) => _done.Any(move => move.Kind != Kind.Removed && SamePath(move.From, from));

        // Where a rename from there that is still done went; null for none.
        public string? DestinationOf(string from) =>
            _done.FirstOrDefault(move => move.Kind != Kind.Removed && SamePath(move.From, from)).To;

        private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
