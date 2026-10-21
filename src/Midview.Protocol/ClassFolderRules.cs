using System.Text;

namespace Midview.Protocol;

// Which files of a class folder ("Pasta da turma") the sync looks at, in one
// place for both sides that list one: the student's Session (ClassFolder, what
// it lists for the Master) and the teacher's Master (ClassStore.Files, what it
// compares that listing with). If they ever disagreed, a file one side lists
// and the other leaves out would be pushed or pulled again every round.
//
// Only about names, paths and sizes. Attributes (hidden, system, links) stay
// with each side's own enumeration.
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

    // Whether the subfolders of a folder at that depth are looked into (the class
    // folder itself is depth 1): a path is at most FileCollection.MaxDepth
    // segments, the file name counted, so the other side never rewrites it.
    public static bool CanDescendFrom(int depth) => depth < FileCollection.MaxDepth;
}
