using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// What saving one file from a machine did to the teacher's copy.
public enum ClassFileOutcome
{
    // The copy already had exactly this version.
    Unchanged,

    // Saved under its own path: new, or a newer version of what the machine
    // had last seen there.
    Saved,

    // Someone else changed the same file since this machine last saw it, so
    // this version went beside it, with the machine's name ("notas (PC-05).docx").
    SavedAsCopy,
}

// The teacher's copy of one class's "Pasta da turma":
// Documents\Midview\Turmas\<class>. It's an ordinary folder: the teacher can
// open it, and whatever is put in it goes to the machines at the next
// lesson. Every file keeps the last-write time it came with, which is half of
// how both sides tell versions apart (ClassFolderEntry).
//
// Whatever a machine sends is treated as hostile, as in FileCollector: every
// path goes through ClassFolderFiles.TargetPath, so nothing lands outside the
// folder. Not thread-safe: ClassFolderSyncer calls it under one lock.
public sealed class ClassStore
{
    public ClassStore(string classesDirectory, string className)
    {
        ClassesDirectory = Path.GetFullPath(classesDirectory);
        ClassName = FolderName(className);
        DirectoryPath = Path.Combine(ClassesDirectory, ClassName);
    }

    public static string DefaultClassesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Midview", "Turmas");

    public string ClassesDirectory { get; }

    // As the teacher sees it: the folder's name.
    public string ClassName { get; }

    public string DirectoryPath { get; }

    // Where files from the machines are written before they're saved: beside
    // the classes, never listed as one, on the same drive so saving is a move.
    public string StagingDirectory => Path.Combine(ClassesDirectory, ".recebendo");

    // The classes the teacher has had so far, by name, in natural order
    // ("6º A" before "10º A").
    public static IReadOnlyList<string> ListClasses(string classesDirectory)
    {
        if (!Directory.Exists(classesDirectory))
        {
            return Array.Empty<string>();
        }
        return new DirectoryInfo(classesDirectory)
            .EnumerateDirectories()
            .Where(directory => !directory.Name.StartsWith('.') && (directory.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) == 0)
            .Select(directory => directory.Name)
            .OrderBy(name => name, NaturalOrder.Instance)
            .ToList();
    }

    // The folder a class is kept in: its name made safe for Windows.
    public static string FolderName(string className) => FileNames.Safe(className.Trim());

    public IReadOnlyList<ClassFolderFile> ListFiles() => ClassFolderFiles.List(DirectoryPath);

    // The version saved at path, or null if there's no file there.
    public ClassFolderEntry? VersionAt(string path)
    {
        var target = ClassFolderFiles.TargetPath(DirectoryPath, path);
        if (target is null || !File.Exists(target))
        {
            return null;
        }
        var file = new FileInfo(target);
        return new ClassFolderEntry(path, file.Length, file.LastWriteTimeUtc.Ticks);
    }

    // Saves a file a machine sent (complete, in tempPath, which is moved away
    // or deleted). seenBefore is the version that machine had at that path
    // the last time it was saved from (null if none): when the copy still
    // has exactly that, or nothing, the machine's version replaces it; when
    // someone else changed it since, nothing is lost: the machine's version
    // goes beside it under the machine's name. created gets each path this
    // added to the copy.
    public (ClassFileOutcome Outcome, string SavedPath) Accept(
        ClassFolderEntry received, string tempPath, ClassFolderEntry? seenBefore, string machineName, Action<string>? created = null)
    {
        try
        {
            var target = ClassFolderFiles.TargetPath(DirectoryPath, received.Path)
                         ?? throw new InvalidDataException($"caminho inválido: {received.Path}");
            File.SetLastWriteTimeUtc(tempPath, new DateTime(received.LastWriteTicks, DateTimeKind.Utc));
            var current = VersionAt(received.Path);
            if (current is not null && current.SameVersionAs(received))
            {
                return (ClassFileOutcome.Unchanged, received.Path);
            }
            if (!Directory.Exists(target) && (current is null || current.SameVersionAs(seenBefore)))
            {
                Put(tempPath, target, current is null, created, received.Path);
                return (ClassFileOutcome.Saved, received.Path);
            }

            var copyPath = CopyPathFor(received.Path, machineName);
            var copyTarget = ClassFolderFiles.TargetPath(DirectoryPath, copyPath)
                             ?? throw new InvalidDataException($"caminho inválido: {copyPath}");
            if (Directory.Exists(copyTarget))
            {
                throw new IOException($"já existe uma pasta com o nome {Path.GetFileName(copyTarget)}");
            }
            Put(tempPath, copyTarget, !File.Exists(copyTarget), created, copyPath);
            return (ClassFileOutcome.SavedAsCopy, copyPath);
        }
        finally
        {
            TryDelete(tempPath); // already moved away when saved
        }
    }

    // A file the machine deleted (or renamed): it leaves the copy, into this
    // PC's Recycle Bin, only if the copy still has exactly the version that
    // machine had -- a newer one from someone else stays.
    public bool Remove(ClassFolderEntry seenBefore, IRecycleBin recycleBin)
    {
        var current = VersionAt(seenBefore.Path);
        if (current is null || !current.SameVersionAs(seenBefore))
        {
            return false;
        }
        recycleBin.Recycle(ClassFolderFiles.TargetPath(DirectoryPath, seenBefore.Path)!);
        RemoveEmptyFolders(seenBefore.Path);
        return true;
    }

    // "Apagar só os de hoje": each of these paths (the ones the lesson added)
    // that's still here goes to this PC's Recycle Bin.
    public (int Recycled, IReadOnlyList<string> Problems) RecyclePaths(IEnumerable<string> paths, IRecycleBin recycleBin)
    {
        var recycled = 0;
        var problems = new List<string>();
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var target = ClassFolderFiles.TargetPath(DirectoryPath, path);
            if (target is null || !File.Exists(target))
            {
                continue;
            }
            try
            {
                recycleBin.Recycle(target);
                RemoveEmptyFolders(path);
                recycled++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{path} ({ErrorText.Describe(ex)})");
            }
        }
        return (recycled, problems);
    }

    // "Apagar tudo": everything in the class's folder goes to this PC's
    // Recycle Bin; the folder itself stays, so the class is still listed.
    public (int Recycled, IReadOnlyList<string> Problems) RecycleAll(IRecycleBin recycleBin)
    {
        var recycled = 0;
        var problems = new List<string>();
        if (!Directory.Exists(DirectoryPath))
        {
            return (recycled, problems);
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(DirectoryPath).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                recycleBin.Recycle(entry);
                recycled++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(entry)} ({ErrorText.Describe(ex)})");
            }
        }
        return (recycled, problems);
    }

    // "notas.docx" from PC-05 -> "notas (PC-05).docx", in the same folder.
    public static string CopyPathFor(string path, string machineName)
    {
        var slash = path.LastIndexOf('/');
        var folder = slash < 0 ? string.Empty : path[..(slash + 1)];
        var name = path[(slash + 1)..];
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        return folder + FileNames.Safe($"{stem} ({FileNames.Safe(machineName)}){extension}");
    }

    // Over whatever is at target, keeping the time it came with; a file
    // that's new to the copy is reported through created.
    private static void Put(string tempPath, string target, bool isNew, Action<string>? created, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            File.SetAttributes(target, FileAttributes.Normal); // a read-only file can't be replaced otherwise
        }
        File.Move(tempPath, target, overwrite: true);
        if (isNew)
        {
            created?.Invoke(path);
        }
    }

    // The folders a removed file leaves empty go too, up to the class folder.
    private void RemoveEmptyFolders(string path)
    {
        var directory = Path.GetDirectoryName(ClassFolderFiles.TargetPath(DirectoryPath, path)!);
        var root = Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar);
        while (directory is not null && directory.Length > root.Length
               && directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    return;
                }
                Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
