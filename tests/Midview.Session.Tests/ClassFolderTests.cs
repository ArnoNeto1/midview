using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public partial class ClassFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-classfolder-{Guid.NewGuid():N}");
    private readonly string _desktop;
    private readonly string _setAside;
    private readonly string _outside;
    private readonly List<string> _links = new();
    private readonly List<string> _protected = new();
    private readonly List<string> _closed = new();
    private readonly RecordingRecycleBin _bin;
    private ClassLesson? _lesson = new(1, Guid.NewGuid(), "3º A", 1, false);

    public ClassFolderTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _setAside = Path.Combine(_root, "guardada");
        _outside = Path.Combine(_root, "fora");
        _bin = new RecordingRecycleBin(Path.Combine(_root, "lixeira"));
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        foreach (var folder in _protected.Where(Directory.Exists))
        {
            Unprotect(folder);
        }
        if (_closed.Count > 0)
        {
            OpenEverythingBelow(_root); // a folder closed to us can't be deleted, nor can what it holds
        }
        // Links first: deleting one removes the link, never what it points at.
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Guid LessonId => _lesson!.LessonId;

    // The folder the Agent keeps on the Desktop for the current lesson.
    private string LessonFolder => Path.Combine(_desktop, _lesson!.FolderName);

    private string MakeLessonFolder()
    {
        Directory.CreateDirectory(LessonFolder);
        return LessonFolder;
    }

    private ClassFolder NewClassFolder(string? desktop = null, int listingPartSize = ClassFolderWire.MaxEntries) =>
        new(desktop ?? _desktop, _setAside, () => _lesson, _bin, Path.Combine(_root, "session.json"), listingPartSize);

    private string SetAsideFolder(Guid lessonId) => Path.Combine(_setAside, lessonId.ToString("N"));

    private string StatePath => Path.Combine(_root, "session.json");

    // The finished lessons the state file still has to settle, oldest first.
    private (Guid LessonId, string Turma)[] Pending()
    {
        if (!File.Exists(StatePath))
        {
            return Array.Empty<(Guid, string)>();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        if (!document.RootElement.TryGetProperty("Pending", out var pending) || pending.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<(Guid, string)>();
        }

        return pending.EnumerateArray()
            .Select(entry => (entry.GetProperty("LessonId").GetGuid(), entry.GetProperty("Turma").GetString()!))
            .ToArray();
    }

    // A folder the student can't look into, as an ACL can make it: listing it is "access denied".
    private void CloseToTheStudent(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(NoListing());
        info.SetAccessControl(security);
        _closed.Add(folder);
    }

    private static void OpenToTheStudent(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.RemoveAccessRuleSpecific(NoListing());
        info.SetAccessControl(security);
    }

    private static FileSystemAccessRule NoListing() =>
        new(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);

    // For the cleanup of a test that failed with a folder still closed: opens each folder before looking inside it.
    private static void OpenEverythingBelow(string folder)
    {
        try
        {
            OpenToTheStudent(folder);
            foreach (var subfolder in Directory.EnumerateDirectories(folder))
            {
                OpenEverythingBelow(subfolder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    // What the Session's state file says it follows, or null without a file.
    private (Guid LessonId, string Turma)? Remembered()
    {
        if (!File.Exists(StatePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        return (document.RootElement.GetProperty("LessonId").GetGuid(), document.RootElement.GetProperty("Turma").GetString()!);
    }

    // Until the Agent releases it, the student can't delete the lesson's folder: a read-only folder says "access denied" the same way.
    private void Protect(string folder)
    {
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
        _protected.Add(folder);
    }

    private static void Unprotect(string folder) =>
        File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);

    private static string Write(string folder, string relativePath, string content = "x")
    {
        var path = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private void MakeJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J did not make the junction");
        _links.Add(link);
    }

    private sealed class RecordingNotice : IClassFolderNotice
    {
        public List<string> Shown { get; } = new();

        public Exception? Throw { get; set; }

        public void Show(string text)
        {
            Shown.Add(text);
            if (Throw is not null)
            {
                throw Throw;
            }
        }
    }

    // Takes what it's given out of the way, as the Recycle Bin does, and remembers the paths.
    private sealed class RecordingRecycleBin : IRecycleBin
    {
        private readonly string _binFolder;

        public RecordingRecycleBin(string binFolder) => _binFolder = binFolder;

        public List<string> Recycled { get; } = new();

        // What was inside each folder when it went.
        public Dictionary<string, string[]> Contents { get; } = new();

        public Exception? Refuse { get; set; }

        public void Recycle(string path)
        {
            if (Refuse is not null)
            {
                throw Refuse;
            }

            if (Directory.Exists(path))
            {
                // The Agent's folder, until it lets go of it, can't be deleted by the student: the Bin can't have it either.
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                {
                    throw new UnauthorizedAccessException($"{path} is protected");
                }

                // What went with the folder. A folder that can't be looked into goes with it unseen, as with the shell's one move.
                try
                {
                    Contents[path] = Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories)
                        .Select(entry => Path.GetRelativePath(path, entry).Replace('\\', '/'))
                        .OrderBy(entry => entry, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (UnauthorizedAccessException)
                {
                }

                Directory.CreateDirectory(_binFolder);
                Directory.Move(path, Path.Combine(_binFolder, Guid.NewGuid().ToString("N")));
            }
            else
            {
                File.Delete(path);
            }

            Recycled.Add(path);
        }
    }
}
