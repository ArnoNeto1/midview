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
        return new IncomingFile(this, FileNames.Safe(requestedName), tempPath);
    }

    // Never overwrites: "notas.pdf" becomes "notas (2).pdf", "notas (3).pdf"...
    internal string MoveToUniqueName(string tempPath, string safeName) =>
        FileNames.MoveToUniqueName(tempPath, DirectoryPath, safeName);
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
