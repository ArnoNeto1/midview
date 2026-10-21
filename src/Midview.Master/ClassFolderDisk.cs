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

    // Everything, hidden and system entries included; a folder that can't be
    // read is an error: for deleting a whole class.
    public static readonly EnumerationOptions EverythingOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    // The files the Session would list for that folder, by '/'-separated
    // path, each with its version. Manual recursion to stop at
    // FileCollection.MaxDepth (the class folder is depth 1), as
    // ClassFolder.ListedFiles does.
    public static Dictionary<string, FileVersion> Scan(DirectoryInfo folder)
    {
        var found = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
        Scan(folder, string.Empty, 1, found);
        return found;
    }

    private static void Scan(DirectoryInfo directory, string prefix, int depth, Dictionary<string, FileVersion> found)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", ListedOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // gone, or closed to us, while we looked
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo subdirectory)
            {
                if (ClassFolderRules.CanDescendFrom(depth) && ClassFolderRules.IsAddressable(subdirectory.Name)
                    && !IsLink(subdirectory))
                {
                    Scan(subdirectory, prefix + subdirectory.Name + "/", depth + 1, found);
                }
            }
            else if (entry is FileInfo file && ClassFolderRules.IsListedFile(file.Name, file.Length)
                && ClassFolderRules.FitsTheWire(prefix + file.Name) && !IsLink(file))
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
        if (string.IsNullOrEmpty(relativePath) || IsLink(folder))
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
            if (IsLink(path))
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

    public static bool IsRealDirectory(string path) => Directory.Exists(path) && !IsLink(path);

    // A junction or symbolic link, which could point anywhere, or back at
    // itself. Not any reparse point: OneDrive's placeholder files are
    // reparse points too, and the teacher's Documents folder may well be in
    // OneDrive; those have no link target.
    public static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // can't tell: don't go through it
        }
    }

    // A path that isn't there isn't one; one whose attributes can't be read is taken for one.
    public static bool IsLink(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }

            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            return IsLink(info);
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
