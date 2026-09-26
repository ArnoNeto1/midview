using System.Windows;

namespace Midview.Master;

public partial class App : Application
{
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

        new MainWindow().Show();
    }
}
