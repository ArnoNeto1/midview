namespace Midview.Session;

// A file being received into the class folder: written to a temp file next to
// its target and only moved there, with the time it had on the teacher's PC,
// when complete. Nothing not completed is left behind.
public sealed class ClassFolderIncoming : IDisposable
{
    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly FileStream _stream;
    private bool _completed;

    internal ClassFolderIncoming(string targetPath, string tempPath)
    {
        _targetPath = targetPath;
        _tempPath = tempPath;
        _stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public void Write(ReadOnlySpan<byte> data) => _stream.Write(data);

    // Replaces what is at the target, and gives it lastWriteTicks (UTC ticks).
    // An IOException (the old file held open by a program, say) propagates
    // and the temp file is deleted by Dispose, so the old file stays as it was.
    public void Complete(long lastWriteTicks)
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();

        // Before the move, so the file never shows up under its real name with
        // a time the Master didn't send (it would take that for the student's
        // own edit). The move is within one folder, and keeps the time.
        File.SetLastWriteTimeUtc(_tempPath, new DateTime(lastWriteTicks, DateTimeKind.Utc));
        try
        {
            File.Move(_tempPath, _targetPath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex) when (File.Exists(_targetPath))
        {
            // Windows answers "access denied", not "in use", when a program holds
            // the old file open without sharing it for replacing. As a sharing
            // violation, ErrorText words it for the teacher.
            throw new IOException($"{Path.GetFileName(_targetPath)} não pôde ser substituído: {ex.Message}",
                unchecked((int)0x80070020));
        }
        _completed = true;
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
