using System.Text;

namespace Midview.Protocol;

// Which files of a class folder ("Pasta da turma") the sync looks at, in one
// place for both sides that list one: the student's Session (ClassFolder, what
// it lists for the Master) and the teacher's Master (ClassStore.Files, what it
// compares that listing with). If they ever disagreed, a file one side lists
// and the other leaves out would be pushed or pulled again every round.
//
// About names, paths, sizes and what counts as a link. The hidden and system
// attributes stay with each side's own enumeration.
public static class ClassFolderRules
{
    // Office's lock file for an open document ("~$trabalho.docx").
    public const string LockFilePrefix = "~$";

    // Midview's own temp files, "<prefix><guid><suffix>": a file being received
    // is written under such a name and moved into place once complete.
    public const string TempPrefix = ".midview-";
    public const string TempSuffix = ".parcial";

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    // A file still being written is never listed (and so never copied
    // half-written): Chrome's ".crdownload", an Office or editor ".tmp", a
    // ".part" and Midview's ".parcial".
    public static IReadOnlyList<string> SkippedSuffixes { get; } =
        Array.AsReadOnly(new[] { ".crdownload", ".tmp", ".parcial", ".part" });

    public static string NewTempName() => $"{TempPrefix}{Guid.NewGuid():N}{TempSuffix}";

    public static bool IsTempName(string name) =>
        name.StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);

    // Whether a file of that name and size is listed: not a lock file, not one
    // of Midview's own (the prefix, whatever follows), not one still being
    // written, small enough for FileTransfer, and with a name that can be
    // found again.
    public static bool IsListedFile(string name, long size) =>
        !name.StartsWith(LockFilePrefix, StringComparison.Ordinal)
        && !name.StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase)
        && !SkippedSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        && size <= FileTransfer.MaxFileSize
        && IsAddressable(name);

    // Every message names a file by a path run through
    // FileCollection.SafeRelativePath; a name it would rewrite (one over
    // FileNames.MaxNameLength, say) could never be found again. For a
    // folder's name as well as a file's.
    public static bool IsAddressable(string name) =>
        string.Equals(FileNames.Safe(name), name, StringComparison.Ordinal);

    // What ClassFolderWire.EncodeListing takes as a path: '/'-separated, at most
    // FileTransfer.MaxNameBytes of UTF-8.
    public static bool FitsTheWire(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && StrictUtf8.GetByteCount(path) <= FileTransfer.MaxNameBytes;
        }
        catch (EncoderFallbackException)
        {
            return false; // a name with half a surrogate pair, which NTFS allows
        }
    }

    // A path of that many segments, the file name counted, is at most
    // FileCollection.MaxDepth, so the other side never rewrites it.
    public static bool FitsTheDepth(int segmentCount) => segmentCount <= FileCollection.MaxDepth;

    // Whether the subfolders of a folder at that depth are looked into (the class
    // folder itself is depth 1): the files in them are one segment deeper.
    public static bool CanDescendFrom(int depth) => FitsTheDepth(depth + 1);

    // A file set aside inside the class folder while a newer copy takes its
    // place: never listed (the Midview prefix), and not a temp name either,
    // since what ends a lesson throws temps away and this is somebody's work.
    // The name is kept at the end, so that the Recycle Bin shows what it is.
    public static string NewAsideName(string name) => $"{TempPrefix}{Guid.NewGuid():N}-{name}";

    // The name for the copy-th file of that name ("images (2).jpg"), the stem
    // shortened as much as it takes for the result to stay addressable (a name
    // of FileNames.MaxNameLength characters has no room for " (2)") and for its
    // path, after directoryPrefix ("fotos/"), to fit the wire. Null if no stem
    // is short enough (a very long extension).
    public static string? CopyName(string name, int copy, string directoryPrefix = "")
    {
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        if (stem.Length == 0)
        {
            // A name that is all "extension" (".gitignore"): ".gitignore (2)".
            stem = name;
            extension = string.Empty;
        }

        var suffix = $" ({copy})";
        while (true)
        {
            var candidate = stem + suffix + extension;
            if (IsAddressable(candidate) && FitsTheWire(directoryPrefix + candidate))
            {
                return candidate;
            }
            if (stem.Length == 0)
            {
                return null;
            }

            var pair = stem.Length > 1 && char.IsLowSurrogate(stem[^1]) && char.IsHighSurrogate(stem[^2]);
            stem = stem[..^(pair ? 2 : 1)];
        }
    }

    // Why a file that arrives at that '/'-separated path, of that size, would
    // not be listed by either side (in the teacher's words), or null when it
    // would. A file stored where it is not listed would be copied again and
    // again, every round.
    public static string? WhyNotListed(string relativePath, long size)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return "o caminho está vazio";
        }

        var segments = relativePath.Split('/');
        if (!FitsTheDepth(segments.Length))
        {
            return $"há pastas demais (no máximo {FileCollection.MaxDepth} níveis, contando o arquivo)";
        }

        foreach (var folder in segments[..^1])
        {
            if (!IsAddressable(folder))
            {
                return $"o nome da pasta \"{folder}\" não é aceito";
            }
        }

        if (size > FileTransfer.MaxFileSize)
        {
            return "o arquivo é grande demais";
        }
        if (!IsListedFile(segments[^1], size))
        {
            return "o nome do arquivo não é aceito (temporário, em uso ou fora do padrão)";
        }
        return FitsTheWire(relativePath) ? null : "o caminho é longo demais";
    }

    // A junction or symbolic link, which could point anywhere, or back at
    // itself: what neither side goes through. Not every reparse point: OneDrive's
    // placeholder files are reparse points too, and the Desktop or Documents
    // folder may well be in OneDrive; those, like a compressed file's, have no
    // link target and are ordinary files. One whose link target can't be read
    // is taken for a link.
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

    // The same for a path. One that isn't there isn't a link; one whose
    // attributes can't be read is taken for one.
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
}
