using System.Diagnostics;

namespace Midview.Session;

// Opens a file with the program Windows opens it with, or a folder in File
// Explorer, on the student's screen ("Depois de enviar" in "Enviar arquivo").
public interface IFileOpener
{
    // Throws if Windows couldn't open it (no program for that kind of file).
    void Open(string path);
}

// As a double-click would, on a short-lived STA thread: some shell handlers
// need one, and Session's own threads aren't.
public sealed class WindowsFileOpener : IFileOpener
{
    public void Open(string path) => ShellStart(new ProcessStartInfo(path) { UseShellExecute = true });

    // Starts it through the shell on that thread. False when Windows is
    // still at it after 15 s (a program asking for an administrator's
    // password, say); throws what Windows refused it with.
    internal static bool ShellStart(ProcessStartInfo startInfo)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var _ = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Open for the student",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(15));
        if (failure is not null)
        {
            throw failure;
        }
        return finished;
    }
}
