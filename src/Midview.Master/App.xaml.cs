using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace Midview.Master;

public partial class App : Application
{
    // What the setup's AppMutex looks for, to ask that the program be
    // closed before it is replaced. Kept for the process's lifetime.
    private static readonly List<Mutex> RunningMutexes = new();

    // How "Atualizar professor" ended, when its script opened this Midview
    // again (TeacherUpdate.FinishedArgument), shown once the classroom is up;
    // and whether it put the new version here, so the students are next.
    private string? _updateNotice;
    private bool _updatedWell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is [TeacherUpdate.FinishedArgument, var exitCode, var version])
        {
            _updateNotice = TeacherUpdate.FinishedNotice(exitCode, version, TeacherUpdate.CurrentVersion, TeacherUpdate.Folder);
            _updatedWell = TeacherUpdate.FinishedWell(exitCode, version, TeacherUpdate.CurrentVersion);
        }

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
        // Once the classroom is drawn, and only once: how the update just
        // made ended (then the students), or else whether GitHub has a newer
        // version. Never both, so a Midview just updated doesn't ask again.
        void OnShown(object? sender, EventArgs e)
        {
            window.ContentRendered -= OnShown;
            if (_updateNotice is { } notice)
            {
                MessageBox.Show(window, notice, "Atualizar professor", MessageBoxButton.OK, MessageBoxImage.Information);
                if (_updatedWell)
                {
                    window.OfferStudentUpdate();
                }
            }
            else
            {
                window.OfferTeacherUpdate();
            }
        }
        window.ContentRendered += OnShown;
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
