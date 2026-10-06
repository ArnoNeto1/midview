using System.Diagnostics;

namespace Midview.Session;

// Starts a program on the student's screen, as the student ("Abrir
// programa").
public interface IProgramStarter
{
    // True once Windows has started it; false when Windows is still at it
    // after a while (usually a program asking for an administrator's
    // password on the student's screen). Throws what Windows refused it
    // with: a Win32Exception, 2 for a program it couldn't find.
    bool Start(string program, string arguments);
}

// As Win+R would: Windows also finds "chrome" or "winword" through their App
// Paths, not only along PATH. It starts in the student's own folder rather
// than Midview's install folder, Session's working directory.
public sealed class WindowsProgramStarter : IProgramStarter
{
    public bool Start(string program, string arguments) =>
        WindowsFileOpener.ShellStart(new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        });
}
