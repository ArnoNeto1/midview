using System.Diagnostics;
using Midview.Protocol;

namespace Midview.Session;

public sealed class WindowsOpenPrograms : IOpenProgramsLister
{
    public IReadOnlyList<OpenProgram> List()
    {
        using var self = Process.GetCurrentProcess();
        var described = new Dictionary<int, (string, string)?>();
        return OpenProgramsList.Build(WindowsAppCloser.EnumerateTopLevelWindows(), self.Id, processId =>
        {
            if (described.TryGetValue(processId, out var cached))
            {
                return cached;
            }
            var result = Describe(processId);
            described[processId] = result;
            return result;
        });
    }

    private static (string ExeName, string DisplayName)? Describe(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var exeName = Path.GetFileName(path);
            string? description = null;
            try
            {
                description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // No version information: the exe name is shown instead.
            }
            return (exeName, string.IsNullOrWhiteSpace(description) ? exeName : description);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Gone, or a process this token can't read (elevated / another user).
            return null;
        }
    }
}
