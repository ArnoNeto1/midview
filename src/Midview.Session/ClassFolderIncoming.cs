namespace Midview.Session;

// A file being received into the class folder: written to a temp file next to
// its target and only moved there, with the time it had on the teacher's PC,
// when complete, and only onto what the Master expects to replace there (no
// file, or the version its listing showed). Nothing not completed is left
// behind.
public sealed class ClassFolderIncoming : IDisposable
{
    // The refusal when the file at the target isn't what the Master expects.
    internal const string Changed = "mudou neste PC";

    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly (long Size, long LastWriteTicks)? _replacing;
    private readonly FileStream _stream;
    private bool _completed;

    internal ClassFolderIncoming(string targetPath, string tempPath, (long Size, long LastWriteTicks)? replacing)
    {
        _targetPath = targetPath;
        _tempPath = tempPath;
        _replacing = replacing;
        _stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public void Write(ReadOnlySpan<byte> data) => _stream.Write(data);

    // Puts the file at the target, giving it lastWriteTicks (UTC ticks): where
    // nothing was expected, only if nothing is there (one move that never
    // overwrites); otherwise only if the file there still has the expected
    // version, looked at just before it is replaced. If not, it throws
    // InvalidOperationException ("mudou neste PC") and the student's file stays.
    // An IOException (the old file held open by a program, say) propagates.
    // Either way the temp file is deleted by Dispose and the old file stays as
    // it was.
    public void Complete(long lastWriteTicks)
    {
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();

        // Before the move, so the file never shows up under its real name with
        // a time the Master didn't send (it would take that for the student's
        // own edit). The move is within one folder, and keeps the time.
        File.SetLastWriteTimeUtc(_tempPath, new DateTime(lastWriteTicks, DateTimeKind.Utc));
        if (_replacing is null)
        {
            try
            {
                File.Move(_tempPath, _targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(_targetPath) || Directory.Exists(_targetPath))
            {
                throw new InvalidOperationException(Changed);
            }
        }
        else
        {
            if (!IsAsExpected(_targetPath, _replacing))
            {
                throw new InvalidOperationException(Changed);
            }

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

    // Whether path is what a push expects there: no file when replacing is
    // null, otherwise a file of that version.
    internal static bool IsAsExpected(string path, (long Size, long LastWriteTicks)? replacing) =>
        replacing is { } version ? HasVersion(path, version) : !File.Exists(path);

    // Whether there is a file at path with that size and last write time (UTC
    // ticks), as a listing shows it.
    internal static bool HasVersion(string path, (long Size, long LastWriteTicks) version)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == version.Size && file.LastWriteTimeUtc.Ticks == version.LastWriteTicks;
    }
}
