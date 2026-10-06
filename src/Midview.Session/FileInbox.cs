using Midview.Protocol;

namespace Midview.Session;

// Where files from the teacher land: one folder, sanitized and unique names,
// written through a temp file so a half-received file never shows up under
// its real name. Nothing here ever opens or runs what it saves.
public sealed class FileInbox
{
    public FileInbox(string directory)
    {
        DirectoryPath = directory;
    }

    // Session runs as the student, so this is their own Desktop.
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StudentFolderNames.Inbox);

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
            // The folder is named: where it pointed is what showed the
            // first live run that Session had no profile to put it in.
            return $"a pasta de destino ({DirectoryPath}) não pôde ser usada: {ErrorText.Describe(ex)}";
        }
    }

    public IncomingFile Begin(string requestedName)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tempPath = Path.Combine(DirectoryPath, $".midview-{Guid.NewGuid():N}.parcial");
        return new IncomingFile(this, FileNames.Safe(requestedName), tempPath);
    }

    // "notas.pdf" becomes "notas (2).pdf", "notas (3).pdf"... unless the
    // teacher chose to replace it. An old copy open in a program can't be
    // replaced, so the new one is saved beside it, and the note says so.
    internal (string Name, string? Note) Save(string tempPath, string safeName, bool overwrite)
    {
        var target = Path.Combine(DirectoryPath, safeName);
        if (!overwrite || !File.Exists(target))
        {
            return (FileNames.MoveToUniqueName(tempPath, DirectoryPath, safeName), null);
        }

        try
        {
            File.Move(tempPath, target, overwrite: true);
            return (safeName, "substituiu o anterior");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (FileNames.MoveToUniqueName(tempPath, DirectoryPath, safeName),
                "o anterior estava aberto e não pôde ser substituído");
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
    public string Complete() => Complete(overwrite: false).Name;

    // The name it was saved under, and a note for the teacher when it isn't
    // simply that (a file replaced, or one that couldn't be).
    public (string Name, string? Note) Complete(bool overwrite)
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        var saved = _inbox.Save(_tempPath, _safeName, overwrite);
        _completed = true;
        return saved;
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
