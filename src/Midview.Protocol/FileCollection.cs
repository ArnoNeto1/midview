namespace Midview.Protocol;

// Collecting the files students put in their "Entregar ao professor" folder.
// The Master opens the connection with CollectFiles (empty payload). The
// Session answers with each file in turn -- a SendFile header (FileTransfer:
// size, then the path relative to that folder, '/'-separated) followed by
// FileChunk messages adding up to exactly that size -- and ends with one
// CommandAck carrying a CommandResult: success, with the detail listing any
// file it couldn't send, or why nothing could be collected. The Master never
// answers in between; it stops a collection by closing the connection.
//
// Both sides enforce the limits: the Session so a well-behaved collection
// stays within them, the Master because a student could stand in for their
// own machine's Session (see SessionIdentity) and send anything.
public static class FileCollection
{
    public const int MaxFiles = 1000;

    // Per machine per collection: 25 machines stay under ~12 GB at worst.
    public const long MaxTotalBytes = 500L * 1024 * 1024;

    // Path segments, the file name included: the folder itself and 7 levels
    // of subfolders.
    public const int MaxDepth = 8;

    // The path the other side sent, as safe segments to save under: "." and
    // ".." dropped (so nothing can climb out), every segment through
    // FileNames.Safe, at most MaxDepth of them (the last ones).
    public static IReadOnlyList<string> SafeRelativePath(string requested)
    {
        var segments = requested
            .Split('/', '\\')
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0 && segment != "." && segment != "..")
            .Select(FileNames.Safe)
            .ToList();

        if (segments.Count == 0)
        {
            return new[] { "arquivo" };
        }
        return segments.Skip(Math.Max(0, segments.Count - MaxDepth)).ToList();
    }
}
