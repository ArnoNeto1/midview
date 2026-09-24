using Midview.Protocol;

namespace Midview.Session;

// One file the teacher's "Recolher arquivos" would collect: where it is,
// and its '/'-separated path inside the delivery folder.
public sealed record CollectableFile(string FullPath, string RelativePath);

// The student's "Entregar ao professor" folder: what they put in it is what
// the teacher collects (see FileCollection). Nothing else on the machine is
// read for it, and nothing is ever removed from it.
public sealed class DeliveryFolder
{
    // Hidden and system entries are skipped, e.g. the "~$trabalho.docx"
    // lock files Office leaves next to an open document.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    public DeliveryFolder(
        string directory, int maxFiles = FileCollection.MaxFiles, long maxTotalBytes = FileCollection.MaxTotalBytes)
    {
        DirectoryPath = directory;
        MaxFiles = maxFiles;
        MaxTotalBytes = maxTotalBytes;
    }

    // Session runs as the student, so this is their own Desktop.
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Entregar ao professor");

    public string DirectoryPath { get; }

    public int MaxFiles { get; }

    public long MaxTotalBytes { get; }

    // Created when the student logs in, so it's there to put work into
    // before the teacher asks for it.
    public void TryCreate()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not create the delivery folder {DirectoryPath}: {ex.Message}");
        }
    }

    // In path order, subfolders included down to FileCollection.MaxDepth,
    // and at most MaxFiles + 1 (enough to tell there were more). Linked
    // folders (junctions, symbolic links) are skipped: they could point
    // anywhere, or back at themselves.
    public IReadOnlyList<CollectableFile> ListFiles()
    {
        var files = new List<CollectableFile>();
        if (Directory.Exists(DirectoryPath))
        {
            Walk(new DirectoryInfo(DirectoryPath), prefix: string.Empty, depth: 1, files);
        }
        return files;
    }

    private void Walk(DirectoryInfo directory, string prefix, int depth, List<CollectableFile> files)
    {
        foreach (var file in directory.EnumerateFiles("*", Options).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (files.Count > MaxFiles)
            {
                return;
            }
            files.Add(new CollectableFile(file.FullName, prefix + file.Name));
        }

        if (depth >= FileCollection.MaxDepth)
        {
            return;
        }

        var subdirectories = directory.EnumerateDirectories("*", Options)
            .Where(subdirectory => (subdirectory.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(subdirectory => subdirectory.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var subdirectory in subdirectories)
        {
            if (files.Count > MaxFiles)
            {
                return;
            }
            Walk(subdirectory, prefix + subdirectory.Name + "/", depth + 1, files);
        }
    }
}
