using System.IO;
using Midview.Protocol;
using static Midview.Master.ClassFolderDisk;

namespace Midview.Master;

// ClassStore: ending a lesson.
public sealed partial class ClassStore
{
    // --- Ending a lesson ---

    // Applies the teacher's choice to the class's files, which go to the
    // Recycle Bin; what can't go stays, named in Problems with why.
    // Recycled counts the files that did go. The tombstones and the per-PC
    // records are cleared whatever the choice: the next lesson starts from
    // what is in the folder.
    //
    // DeleteToday deletes the files whose arrival day is today (one the index
    // hasn't seen yet arrived the day of its own time, so after a lost index
    // the older files are not taken for today's). DeleteAll deletes everything
    // in the folder, hidden files and the ones the sync leaves out included;
    // a link in it is left alone, and said. Folders left empty are tidied
    // away.
    public (int Recycled, IReadOnlyList<string> Problems) EndLesson(string turma, EndChoice choice)
    {
        var folder = FolderOf(turma);
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice));
        }

        var problems = new List<string>();
        var recycled = 0;
        lock (_gate)
        {
            var index = IndexOf(turma);
            // With the folder not there (a disk that isn't reachable, say) the
            // files are not known to be gone: only what is about the PCs is cleared.
            if (IsRealDirectory(folder))
            {
                DiscardLeftoverTemps(folder);
                if (choice == EndChoice.DeleteAll)
                {
                    recycled = RecycleEverything(new DirectoryInfo(folder), string.Empty, problems);
                }
                else
                {
                    IndexNewFiles(index, Files(turma));
                    if (choice == EndChoice.DeleteToday)
                    {
                        recycled = RecycleArrivedOn(folder, index, Today, problems);
                    }
                }

                foreach (var path in index.ArrivedOn.Keys.ToList())
                {
                    var full = PathInside(folder, path);
                    if (full is null || IsMissing(full))
                    {
                        index.ArrivedOn.Remove(path);
                    }
                }
            }

            index.Deleted.Clear();
            index.Machines.Clear();
            Save(turma, index);
        }
        return (recycled, problems);
    }

    // The files the index has as arrived on that day, to the Recycle Bin.
    private int RecycleArrivedOn(string folder, ClassIndex index, DateOnly day, List<string> problems)
    {
        var recycled = 0;
        var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = index.ArrivedOn.Where(entry => entry.Value == day).Select(entry => entry.Key)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var path in paths)
        {
            var full = PathInside(folder, path);
            if (full is not null && File.Exists(full) && TryRecycle(full, path, problems))
            {
                recycled++;
                emptied.Add(Path.GetDirectoryName(full)!);
            }
        }

        foreach (var directory in emptied)
        {
            PruneEmptyUpTo(folder, directory);
        }
        return recycled;
    }

    // Every file below the directory, and the folders left empty. In-progress
    // transfers are left alone: DiscardLeftoverTemps already took the ones
    // nothing is writing.
    private int RecycleEverything(DirectoryInfo directory, string prefix, List<string> problems)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", EverythingOptions)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{(prefix.Length == 0 ? directory.Name : prefix.TrimEnd('/'))} ({ErrorText.Describe(ex)})");
            return 0;
        }

        var recycled = 0;
        foreach (var entry in entries)
        {
            var path = prefix + entry.Name;
            if (IsLink(entry))
            {
                problems.Add($"{path} (é um atalho; não foi apagado)");
            }
            else if (entry is DirectoryInfo subdirectory)
            {
                recycled += RecycleEverything(subdirectory, path + "/", problems);
                TryDeleteIfEmpty(subdirectory.FullName);
            }
            else if (!ClassFolderRules.IsTempName(entry.Name) && TryRecycle(entry.FullName, path, problems))
            {
                recycled++;
            }
        }
        return recycled;
    }

    private bool TryRecycle(string fullPath, string path, List<string> problems)
    {
        try
        {
            _recycleBin.Recycle(fullPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{path} ({ErrorText.Describe(ex)})");
            return false;
        }
    }
}
