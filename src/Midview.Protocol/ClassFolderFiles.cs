namespace Midview.Protocol;

// One file in a "Pasta da turma" (the student's, or the teacher's copy).
public sealed record ClassFolderFile(string FullPath, ClassFolderEntry Entry);

// How both sides list a "Pasta da turma" and find where a listed path goes,
// so a path always means the same file on the student's machine and on the
// teacher's PC.
public static class ClassFolderFiles
{
    // Hidden and system entries are skipped, e.g. the "~$trabalho.docx" lock
    // files Office leaves next to an open document, and so are files still
    // arriving (TempPrefix).
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    private const string TempPrefix = ".midview-";

    // A name for a file still being written, never listed.
    public static string NewTempName() => $"{TempPrefix}{Guid.NewGuid():N}.parcial";

    // Every file, in path order, subfolders included down to
    // FileCollection.MaxDepth, each under its '/'-separated path. Linked
    // folders (junctions, symbolic links) are skipped: they could point
    // anywhere, or back at themselves. A file or folder whose own name isn't
    // already safe (FileNames.Safe; only another program could have made it)
    // is left out, so every listed path can be saved as it is on the other
    // side, and so is a path too long for a listing (FileTransfer.MaxNameBytes).
    public static IReadOnlyList<ClassFolderFile> List(string directory)
    {
        var files = new List<ClassFolderFile>();
        if (Directory.Exists(directory))
        {
            Walk(new DirectoryInfo(directory), new List<string>(), files);
        }
        return files;
    }

    // Where the file listed under relativePath goes inside root, or null if
    // that isn't a path List could have given (not safe as it is, or too
    // deep) -- so nothing can be written outside root, or under a second name.
    public static string? TargetPath(string root, string relativePath)
    {
        var segments = FileCollection.SafeRelativePath(relativePath);
        if (ClassFolderSync.PathOf(segments) != relativePath || segments.Any(segment => segment.StartsWith(TempPrefix, StringComparison.Ordinal)))
        {
            return null;
        }
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(new[] { fullRoot }.Concat(segments).ToArray()));
        return target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? target : null;
    }

    private static void Walk(DirectoryInfo directory, List<string> segments, List<ClassFolderFile> files)
    {
        foreach (var file in directory.EnumerateFiles("*", Options).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (file.Name.StartsWith(TempPrefix, StringComparison.Ordinal) || !IsSafeName(file.Name))
            {
                continue;
            }
            try
            {
                var path = ClassFolderSync.PathOf(segments.Append(file.Name).ToList());
                if (System.Text.Encoding.UTF8.GetByteCount(path) > FileTransfer.MaxNameBytes)
                {
                    continue; // too long to name in a listing
                }
                files.Add(new ClassFolderFile(file.FullName, new ClassFolderEntry(path, file.Length, file.LastWriteTimeUtc.Ticks)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone or unreadable since the listing: left for the next one.
            }
        }

        // A file in a subfolder would be one segment deeper than MaxDepth allows.
        if (segments.Count + 2 > FileCollection.MaxDepth)
        {
            return;
        }
        foreach (var subdirectory in directory.EnumerateDirectories("*", Options).OrderBy(dir => dir.Name, StringComparer.OrdinalIgnoreCase))
        {
            if ((subdirectory.Attributes & FileAttributes.ReparsePoint) != 0 || !IsSafeName(subdirectory.Name)
                || subdirectory.Name.StartsWith(TempPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            segments.Add(subdirectory.Name);
            Walk(subdirectory, segments, files);
            segments.RemoveAt(segments.Count - 1);
        }
    }

    private static bool IsSafeName(string name) => FileNames.Safe(name) == name;
}
