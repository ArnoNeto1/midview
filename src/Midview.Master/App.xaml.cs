using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace Midview.Master;

public partial class App : Application
{
    // What the setup's AppMutex looks for, to ask that the program be
    // closed before it is replaced. Kept for the process's lifetime.
    private static readonly List<Mutex> RunningMutexes = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Run by the Windows scheduled task that wakes the class (WakeTask):
        // no window, just the packets, then exit.
        if (e.Args is [WakeTask.CommandLineArgument, var wakeTime])
        {
            Shutdown(WakeTask.RunFromCommandLine(
                wakeTime, DateTime.Now, KnownMachinesFile.DefaultPath, WakeTask.LogPath,
                macAddresses => WakeOnLanSender.Send(macAddresses, WakeOnLanSender.BroadcastTargets(WakeOnLanSender.LocalNetworks()))));
            return;
        }

        // One Midview per Windows session: opening it again brings back the
        // one already open (OtherInstance).
        RunningMutexes.Add(new Mutex(false, "Midview.Master", out var firstHere));
        if (!firstHere)
        {
            OtherInstance.BringBack();
            Shutdown();
            return;
        }
        try
        {
            // Seen from other sessions too (another user logged on).
            RunningMutexes.Add(new Mutex(false, @"Global\Midview.Master"));
        }
        catch (UnauthorizedAccessException)
        {
            // The local one is enough for the setup run by this user.
        }

        // The opening animation, then the classroom. Shown even with
        // Windows' animations turned off, as school PCs often have them for
        // speed: it's short and a click skips it.
        if (!TryShowSplash(OpenClassroom))
        {
            OpenClassroom();
        }
    }

    private void OpenClassroom()
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    // The classroom opens as the splash ends, before it closes, so the
    // program is never left without a window (which would end it).
    private static bool TryShowSplash(Action then)
    {
        try
        {
            var splash = new SplashWindow();
            splash.Finished += (_, _) => then();
            splash.Show();
            return true;
        }
        catch (Exception ex) when (ex is IOException or XamlParseException)
        {
            return false;
        }
    }
}
