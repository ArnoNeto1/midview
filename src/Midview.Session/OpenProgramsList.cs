using Midview.Protocol;

namespace Midview.Session;

// Answers "what does the student have open?" (MessageType.ListOpenPrograms).
// Runs as the student, so it reads the student's own processes.
public interface IOpenProgramsLister
{
    IReadOnlyList<OpenProgram> List();
}

// Builds the "what does the student have open?" list for the teacher's
// block-programs window (MessageType.ListOpenPrograms). Pure so it can be
// tested anywhere: the same window filter close-programs uses picks the
// student's real programs, then each is described by its exe name and a
// friendly name. The Win32 lookup is passed in.
public static class OpenProgramsList
{
    public static IReadOnlyList<OpenProgram> Build(
        IReadOnlyList<TopLevelWindow> windows, int ownProcessId, Func<int, (string ExeName, string DisplayName)?> describe)
    {
        var byExe = new Dictionary<string, (string DisplayName, int Windows)>(StringComparer.Ordinal);
        var counted = new HashSet<(int ProcessId, string ExeName)>();

        foreach (var window in AppClosePolicy.WindowsToClose(windows, ownProcessId))
        {
            if (describe(window.ProcessId) is not { } described)
            {
                continue;
            }
            var exeName = AppBlockPolicy.ProgramName(described.ExeName);
            if (exeName is null || ProtectedPrograms.IsProtected(exeName))
            {
                continue;
            }

            var displayName = OpenProgramsReport.CleanDisplayName(described.DisplayName, exeName);
            var existing = byExe.GetValueOrDefault(exeName);
            // One window per (process, program) so many windows of one process
            // count once each, and a program open on several processes adds up.
            var windowsForExe = existing.Windows + (counted.Add((window.ProcessId, exeName)) ? 1 : 0);
            byExe[exeName] = (existing.DisplayName ?? displayName, windowsForExe);
        }

        return byExe
            .Select(entry => new OpenProgram(entry.Key, entry.Value.DisplayName, entry.Value.Windows))
            .OrderBy(program => program.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
