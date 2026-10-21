using System.IO;
using Midview.Protocol;
using static Midview.Master.ClassFolderDisk;

namespace Midview.Master;

// ClassStore: what the index knows, receiving a file, and the teacher's changes in Explorer.
public sealed partial class ClassStore
{
    // --- What the index knows ---

    // The day the file arrived in the class (local date), or null when the
    // index doesn't have it as a file of the class.
    public DateOnly? ArrivedOn(string turma, string path)
    {
        lock (_gate)
        {
            return IndexOf(turma).ArrivedOn.TryGetValue(path, out var day) ? day : null;
        }
    }

    // Whether the index has the path at all: as a file of the class, or as
    // one the teacher deleted (see RefreshTombstones).
    public bool IsKnown(string turma, string path)
    {
        lock (_gate)
        {
            var index = IndexOf(turma);
            return index.ArrivedOn.ContainsKey(path) || index.Deleted.Contains(path);
        }
    }

    // Whether the teacher deleted the file (the index knew it, and it's gone
    // from the disk), so a PC still holding it is told to drop it.
    public bool IsDeleted(string turma, string path)
    {
        lock (_gate)
        {
            return IndexOf(turma).Deleted.Contains(path);
        }
    }

    // The version of the file that PC (by its SessionThumbprint) last got
    // from the store or gave to it, or null if it has never had that path.
    public FileVersion? LastSynced(string turma, string machineKey, string path)
    {
        lock (_gate)
        {
            return IndexOf(turma).Machines.TryGetValue(machineKey, out var records)
                && records.TryGetValue(path, out var version) ? version : null;
        }
    }

    // Only in memory until SaveIndex (or any other change, which saves the
    // whole index): a round records a record per file and per PC, and writing
    // the index for each would cost more than the copying. A record lost to a
    // crash is harmless: the next round finds the PC's file equal to the
    // store's and records it again.
    public void RecordSynced(string turma, string machineKey, string path, FileVersion version)
    {
        lock (_gate)
        {
            var index = IndexOf(turma);
            if (!index.Machines.TryGetValue(machineKey, out var records))
            {
                records = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
                index.Machines[machineKey] = records;
            }
            if (records.TryGetValue(path, out var known) && known == version)
            {
                return;
            }

            records[path] = version;
            index.Dirty = true;
        }
    }

    // Like RecordSynced, for all of that PC's records of the class.
    public void ForgetMachine(string turma, string machineKey)
    {
        lock (_gate)
        {
            var index = IndexOf(turma);
            if (index.Machines.Remove(machineKey))
            {
                index.Dirty = true;
            }
        }
    }

    // Writes the index (atomically) if records changed since it was last
    // written, which is what the sync calls after each PC's round. Does nothing
    // otherwise.
    public void SaveIndex(string turma)
    {
        FolderOf(turma); // refuses a name that is no class name
        lock (_gate)
        {
            if (_indexes.TryGetValue(turma, out var index) && index.Dirty)
            {
                Save(turma, index);
            }
        }
    }

    // --- Receiving a file ---

    // A temp file in the class folder, to write a file received from a PC
    // into; CommitIncoming moves it into place once it is complete (close
    // the stream first). Throws DirectoryNotFoundException if the class has
    // no folder: a class that was deleted is not brought back by a download.
    public FileStream BeginIncoming(string turma)
    {
        var folder = RealFolderOf(turma);
        var tempPath = Path.Combine(folder, ClassFolderRules.NewTempName());
        return new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    // Moves the completed temp file BeginIncoming made to relativePath, with
    // the last write time the PC had, and records the arrival. Returns the
    // path it ended up at ('/'-separated, subfolder included).
    //
    // The path has to be one a listing shows (ClassFolderRules.WhyNotListed:
    // depth, names, size, the wire), or it is refused: a file stored where it
    // isn't listed would be copied again, every round.
    //
    // replace: the path is the same file edited on a PC, and the arrival day
    // stays. The old copy is set aside in the class folder (under a short name that
    // is never listed, ClassFolderRules.NewAsideName), the new one moved in, and the
    // old one sent to the Recycle Bin. If the new one can't be moved in, the old one
    // is put back; if the Recycle Bin refuses, the new copy stays and the old one
    // stays aside, to be sent when the Bin takes it (at the next replace, and at the
    // end of the lesson, which finds it wherever it is): nothing is lost either way.
    //
    // Otherwise it is a new file and never overwrites: a taken name gets a free
    // one in the same subfolder ("images.jpg" is "images (2).jpg", the name
    // shortened where there is no room for the " (2)"), returned for the caller
    // to tell the PC; it arrived today, and a teacher's deletion of that path no
    // longer holds.
    //
    // Throws ArgumentException for a path or temp file that isn't inside the
    // class folder, DirectoryNotFoundException for a class with no folder,
    // and IOException (the temp file is left for the caller to delete) for a
    // path no listing would show, a file that isn't the announced size, a time
    // Windows can't store, an old copy in use, or anything else the disk refuses.
    // Once the file is in place nothing fails any more: an index that can't be
    // saved is kept for SaveIndex to retry.
    public string CommitIncoming(string turma, string tempPath, string relativePath, FileVersion version, bool replace)
    {
        var folder = RealFolderOf(turma);
        var temp = Path.GetFullPath(tempPath);
        if (!IsInside(folder, temp))
        {
            throw new ArgumentException("The incoming file isn't in the class folder.", nameof(tempPath));
        }
        if (version.LastWriteTicks <= FileTimeEpochTicks || version.LastWriteTicks > DateTime.MaxValue.Ticks)
        {
            throw new IOException("O arquivo veio com uma data que o Windows não guarda.");
        }

        lock (_gate)
        {
            var index = IndexOf(turma); // before anything is done: a failure here changes nothing
            var target = PathOf(turma, relativePath);
            if (ClassFolderRules.WhyNotListed(relativePath, version.Size) is { } reason)
            {
                throw new IOException($"O arquivo não pode ficar na turma: {reason}.");
            }
            if (new FileInfo(temp).Length != version.Size)
            {
                throw new IOException("O arquivo recebido não tem o tamanho anunciado.");
            }

            string stored;
            try
            {
                stored = Place(temp, target, relativePath, version, replace);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(ErrorText.Describe(ex), ex);
            }

            if (!replace || !index.ArrivedOn.ContainsKey(stored))
            {
                index.ArrivedOn[stored] = Today;
            }
            index.Deleted.Remove(stored);
            TrySave(turma, index);
            return stored;
        }
    }

    // The disk part of CommitIncoming. Returns the path stored.
    private string Place(string temp, string target, string relativePath, FileVersion version, bool replace)
    {
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        // Before the move, so the file never shows in the store with the wrong time.
        File.SetLastWriteTimeUtc(temp, new DateTime(version.LastWriteTicks, DateTimeKind.Utc));

        var segments = relativePath.Split('/');
        if (!replace)
        {
            var folders = string.Concat(segments[..^1].Select(folder => folder + "/"));
            return folders + MoveToFreeName(temp, directory, folders, segments[^1]);
        }

        RecycleLeftoverAsides();
        if (!File.Exists(target))
        {
            File.Move(temp, target);
            return relativePath;
        }

        string aside;
        do
        {
            aside = Path.Combine(directory, ClassFolderRules.NewAsideName(segments[^1]));
        }
        while (File.Exists(aside) || Directory.Exists(aside)); // 8 hex digits are short, not unique
        File.Move(target, aside);
        try
        {
            File.Move(temp, target);
        }
        catch (Exception)
        {
            PutBack(aside, target);
            throw;
        }

        RecycleAside(aside);
        return relativePath;
    }

    // Moves the temp file to name in the directory or, when that is taken, to the
    // first free "name (2)", "name (3)"... that a listing would still show.
    private static string MoveToFreeName(string temp, string directory, string folders, string name)
    {
        for (var copy = 1; copy <= MaxCopies; copy++)
        {
            var candidate = copy == 1 ? name : ClassFolderRules.CopyName(name, copy, folders);
            if (candidate is null)
            {
                break;
            }

            var target = Path.Combine(directory, candidate);
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }

            try
            {
                File.Move(temp, target);
                return candidate;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                // It appeared between the check and the move: the next name.
            }
        }
        throw new IOException("Não há um nome livre para o arquivo nesta pasta.");
    }

    private const int MaxCopies = 9999;

    private void PutBack(string aside, string target)
    {
        try
        {
            File.Move(aside, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The old copy could not be put back at {target}; it is at {aside}: {ex.Message}");
        }
    }

    private void RecycleAside(string aside)
    {
        try
        {
            _recycleBin.Recycle(aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _asides.Add(aside);
            _log($"The old copy at {aside} could not go to the Recycle Bin yet: {ex.Message}");
        }
    }

    // With _gate held: the copies set aside that the Recycle Bin refused, once more.
    private void RecycleLeftoverAsides()
    {
        foreach (var aside in _asides.ToList())
        {
            if (!File.Exists(aside))
            {
                _asides.Remove(aside); // gone: recycled with the rest of the class, or by the teacher
                continue;
            }

            try
            {
                _recycleBin.Recycle(aside);
                _asides.Remove(aside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still refused: the next time.
            }
        }
    }

    // --- What the teacher does in Explorer ---

    // Brings the index up to date with the folder: a file in it the index
    // hasn't seen is new (added by the teacher, or all of them once the index was
    // lost; it arrived the day of its own time, see ArrivalOf), and one
    // the index knew that is gone becomes a tombstone (the teacher deleted
    // it). A file that is only hidden now, or can't be looked at, is not
    // gone; and with the class folder itself missing nothing is deleted,
    // for it may just be a disk or a folder that isn't reachable.
    public void RefreshTombstones(string turma)
    {
        var folder = FolderOf(turma);
        if (!IsRealDirectory(folder))
        {
            return;
        }

        var present = Files(turma);
        lock (_gate)
        {
            var index = IndexOf(turma);
            var changed = IndexNewFiles(index, present);
            foreach (var path in index.ArrivedOn.Keys.Where(path => !present.ContainsKey(path)).ToList())
            {
                var full = PathInside(folder, path);
                if (full is null)
                {
                    index.ArrivedOn.Remove(path); // not a path of this class at all (a hand-edited index)
                    changed = true;
                }
                else if (IsMissing(full))
                {
                    index.ArrivedOn.Remove(path);
                    index.Deleted.Add(path);
                    changed = true;
                }
            }

            if (changed)
            {
                Save(turma, index);
            }
        }
    }
}
