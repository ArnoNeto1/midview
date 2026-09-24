namespace Midview.Session;

// Where files from the teacher land: one folder, sanitized and unique names,
// written through a temp file so a half-received file never shows up under
// its real name. Nothing here ever opens or runs what it saves.
public sealed class FileInbox
{
    public const int MaxNameLength = 120;

    // Windows' own rules, spelled out rather than taken from
    // Path.GetInvalidFileNameChars() so they're the same wherever this runs.
    private static readonly char[] InvalidNameChars = "<>:\"/\\|?*".ToCharArray();

    private static readonly HashSet<string> ReservedNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(n => $"COM{n}"))
            .Concat(Enumerable.Range(1, 9).Select(n => $"LPT{n}")),
        StringComparer.OrdinalIgnoreCase);

    public FileInbox(string directory)
    {
        DirectoryPath = directory;
    }

    // Session runs as the student, so this is their own Desktop.
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Arquivos do professor");

    public string DirectoryPath { get; }

    // Null when the file can be accepted, otherwise why not.
    public string? RefusalFor(long size)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var root = Path.GetPathRoot(Path.GetFullPath(DirectoryPath));
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace < size)
            {
                return "não há espaço livre suficiente no disco do aluno";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"a pasta de destino não pôde ser usada: {ex.Message}";
        }
    }

    public IncomingFile Begin(string requestedName)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tempPath = Path.Combine(DirectoryPath, $".midview-{Guid.NewGuid():N}.parcial");
        return new IncomingFile(this, SafeFileName(requestedName), tempPath);
    }

    // Only ever a plain file name inside the inbox: the last path segment of
    // whatever was sent ("..\..\x.exe" and "C:\Windows\x.exe" both become
    // "x.exe"), with characters Windows forbids replaced -- ':' included,
    // which also rules out drive letters and NTFS alternate streams.
    public static string SafeFileName(string requested)
    {
        var name = requested.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Select(c => c < 32 || InvalidNameChars.Contains(c) ? '_' : c).ToArray());
        name = name.Trim().TrimEnd('.', ' '); // Windows silently drops trailing dots and spaces

        if (name.Length == 0)
        {
            name = "arquivo";
        }

        if (ReservedNames.Contains(name.Split('.')[0]))
        {
            name = "_" + name; // CON, NUL, COM1... are devices, not files, even with an extension
        }

        if (name.Length > MaxNameLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 20)
            {
                extension = string.Empty;
            }
            name = name[..(MaxNameLength - extension.Length)].TrimEnd('.', ' ') + extension;
        }

        return name;
    }

    // Never overwrites: "notas.pdf" becomes "notas (2).pdf", "notas (3).pdf"...
    internal string MoveToUniqueName(string tempPath, string safeName)
    {
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var copy = 1; ; copy++)
        {
            var candidate = copy == 1 ? safeName : $"{stem} ({copy}){extension}";
            var target = Path.Combine(DirectoryPath, candidate);
            if (File.Exists(target))
            {
                continue;
            }

            try
            {
                File.Move(tempPath, target);
                return candidate;
            }
            catch (IOException) when (File.Exists(target))
            {
                // Appeared between the check and the move; try the next name.
            }
        }
    }
}

public sealed class IncomingFile : IDisposable
{
    private readonly FileInbox _inbox;
    private readonly string _safeName;
    private readonly string _tempPath;
    private readonly FileStream _stream;
    private bool _completed;

    internal IncomingFile(FileInbox inbox, string safeName, string tempPath)
    {
        _inbox = inbox;
        _safeName = safeName;
        _tempPath = tempPath;
        _stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public void Write(ReadOnlySpan<byte> data) => _stream.Write(data);

    // Returns the name the file was saved under.
    public string Complete()
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        var savedName = _inbox.MoveToUniqueName(_tempPath, _safeName);
        _completed = true;
        return savedName;
    }

    // Anything not completed is thrown away.
    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        _stream.Dispose();
        try
        {
            File.Delete(_tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
