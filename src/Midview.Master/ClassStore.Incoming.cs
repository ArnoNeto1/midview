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
    // replace: the path is the same file edited on a PC, so the old copy goes
    // to the Recycle Bin first and the arrival day stays. Otherwise it is a
    // new file and never overwrites: a taken name gets a free one in the
    // same subfolder ("images.jpg" is "images (2).jpg"), returned for the
    // caller to tell the PC; it arrived today, and a teacher's deletion of
    // that path no longer holds.
    //
    // Throws ArgumentException for a path or temp file that isn't inside the
    // class folder, DirectoryNotFoundException for a class with no folder,
    // and IOException (the temp file is left for the caller to delete) for a
    // file that isn't the announced size, a time Windows can't store, an old
    // copy in use, or anything else the disk refuses.
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
            var target = PathOf(turma, relativePath);
            if (new FileInfo(temp).Length != version.Size)
            {
                throw new IOException("O arquivo recebido não tem o tamanho anunciado.");
            }

            var directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            // Before the move, so the file never shows in the store with the wrong time.
            File.SetLastWriteTimeUtc(temp, new DateTime(version.LastWriteTicks, DateTimeKind.Utc));

            var segments = relativePath.Split('/');
            var stored = relativePath;
            if (replace)
            {
                if (File.Exists(target))
                {
                    _recycleBin.Recycle(target);
                }
                File.Move(temp, target);
            }
            else
            {
                var name = FileNames.MoveToUniqueName(temp, directory, segments[^1]);
                stored = string.Join('/', segments[..^1].Append(name));
            }

            var index = IndexOf(turma);
            if (!replace || !index.ArrivedOn.ContainsKey(stored))
            {
                index.ArrivedOn[stored] = Today;
            }
            index.Deleted.Remove(stored);
            Save(turma, index);
            return stored;
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
