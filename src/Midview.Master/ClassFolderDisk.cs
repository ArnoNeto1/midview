using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// What ClassStore asks of the disk: which files of a class folder the sync
// may look at, whether a path stays inside it, and the small cleanups after
// a lesson.
internal static class ClassFolderDisk
{
    private static readonly EnumerationOptions ListedOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    // The same, but a folder that can't be read is an error, not an empty one.
    private static readonly EnumerationOptions StrictListedOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = false,
    };

    // Everything, hidden and system entries included; a folder that can't be
    // read is an error: for deleting a whole class.
    public static readonly EnumerationOptions EverythingOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    // Everything, hidden and system entries included; what can't be read is skipped: for counting.
    private static readonly EnumerationOptions CountedOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
    };

    // How many files there are under the folder, at any depth, hidden and
    // system ones too; never through a link (one that is a file counts as a
    // file). A folder that can't be read counts as none.
    public static int CountFiles(string folder)
    {
        var count = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(folder));
        while (pending.Count > 0)
        {
            List<FileSystemInfo> entries;
            try
            {
                entries = pending.Pop().EnumerateFileSystemInfos("*", CountedOptions).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // gone, or closed to us
            }

            foreach (var entry in entries)
            {
                if (entry is not DirectoryInfo directory)
                {
                    count++;
                }
                else if (!ClassFolderRules.IsLink(directory))
                {
                    pending.Push(directory);
                }
            }
        }
        return count;
    }

    // The files the Session would list for that folder, by '/'-separated
    // path, each with its version. Manual recursion to stop at
    // FileCollection.MaxDepth (the class folder is depth 1), as
    // ClassFolder.ListedFiles does.
    //
    // Strict: a folder or entry that can't be read (other than the kinds left
    // out on purpose, which are never opened) throws IOException, rather than
    // the listing silently missing what is in it: the sync would take those
    // files for gone, or for new. Not strict: it is left out, for a summary
    // that makes do with what it sees.
    public static Dictionary<string, FileVersion> Scan(DirectoryInfo folder, bool strict)
    {
        var found = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
        Scan(folder, string.Empty, 1, found, strict);
        return found;
    }

    private static void Scan(
        DirectoryInfo directory, string prefix, int depth, Dictionary<string, FileVersion> found, bool strict)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", strict ? StrictListedOptions : ListedOptions).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return; // gone while we looked: nothing in it
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!strict)
            {
                return;
            }

            var where = prefix.Length == 0 ? "a pasta da turma" : $"a pasta \"{prefix.TrimEnd('/')}\"";
            throw new IOException($"Não foi possível ler {where}: {ErrorText.Describe(ex)}", ex);
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo subdirectory)
            {
                if (ClassFolderRules.CanDescendFrom(depth) && ClassFolderRules.IsAddressable(subdirectory.Name)
                    && !ClassFolderRules.IsLink(subdirectory))
                {
                    Scan(subdirectory, prefix + subdirectory.Name + "/", depth + 1, found, strict);
                }
            }
            else if (entry is FileInfo file && ClassFolderRules.IsListedFile(file.Name, file.Length)
                && ClassFolderRules.FitsTheWire(prefix + file.Name) && !ClassFolderRules.IsLink(file))
            {
                found[prefix + file.Name] = new FileVersion(file.Length, file.LastWriteTimeUtc.Ticks);
            }
        }
    }

    // folder plus the '/'-separated path, or null when that isn't a file or
    // folder inside it: every segment must be a name FileNames.Safe leaves
    // alone (which rules out "..", ".", drives, streams, device names, empty
    // segments and the like) and no link may be on the way. A piece that
    // doesn't exist (yet) can't be a link.
    public static string? PathInside(string folder, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || ClassFolderRules.IsLink(folder))
        {
            return null;
        }

        var path = folder;
        foreach (var segment in relativePath.Split('/'))
        {
            if (!ClassFolderRules.IsAddressable(segment))
            {
                return null;
            }

            path = Path.Combine(path, segment);
            if (ClassFolderRules.IsLink(path))
            {
                return null;
            }
        }
        return IsInside(folder, path) ? path : null;
    }

    // Strictly inside: the folder itself is not.
    public static bool IsInside(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRealDirectory(string path) => Directory.Exists(path) && !ClassFolderRules.IsLink(path);

    // Whether the file is really gone: not there, or a folder now. A path
    // that can't be looked at (access denied, a sharing error) isn't gone.
    public static bool IsMissing(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The copies of replaced files that CommitIncoming set aside and that are still
    // in the class folder, at any depth (the Recycle Bin refused them, or the Master
    // was closed before they could go). Not through links; hidden folders are not
    // looked into, as they aren't for a listing.
    public static List<FileInfo> FindAsides(DirectoryInfo folder)
    {
        var found = new List<FileInfo>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            List<FileSystemInfo> entries;
            try
            {
                entries = pending.Pop().EnumerateFileSystemInfos("*", ListedOptions).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // gone, or closed to us: a listing says so, this only looks
            }

            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo directory)
                {
                    if (!ClassFolderRules.IsLink(directory))
                    {
                        pending.Push(directory);
                    }
                }
                else if (entry is FileInfo file && ClassFolderRules.IsAsideName(file.Name) && !ClassFolderRules.IsLink(file))
                {
                    found.Add(file);
                }
            }
        }
        return found.OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // The temp files of transfers that never finished (the Master was closed
    // mid-copy). One that is still being written is in use and stays.
    public static void DiscardLeftoverTemps(string folder)
    {
        try
        {
            foreach (var temp in Directory.EnumerateFiles(folder, ClassFolderRules.TempPrefix + "*" + ClassFolderRules.TempSuffix))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use, or not ours to remove: leave it.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to discard that we can see.
        }
    }

    // Removes the directory, and the ones above it up to (not including) the
    // class folder, while they are empty.
    public static void PruneEmptyUpTo(string folder, string directory)
    {
        while (IsInside(folder, directory) && TryDeleteIfEmpty(directory))
        {
            directory = Path.GetDirectoryName(directory)!;
        }
    }

    public static bool TryDeleteIfEmpty(string directory)
    {
        try
        {
            Directory.Delete(directory); // not recursive: refuses a folder that has anything in it
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
