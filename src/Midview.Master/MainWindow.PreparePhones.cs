using System.Windows;

namespace Midview.Master;

// "Preparar celulares" (PreparePhonesWindow): opened by the big button of the
// empty "Celulares" filter and by the link in "Máquinas da sala". It hears
// from here when a prepared phone joins the class (PhoneJoined).
public partial class MainWindow
{
    private PreparePhonesWindow? _preparePhonesWindow;

    private void OnPreparePhonesClicked(object sender, RoutedEventArgs e) => OpenPreparePhonesWindow();

    // One at a time: asking again brings the open one back. The window is
    // returned so a caller can start it at a number (StartAt).
    internal PreparePhonesWindow OpenPreparePhonesWindow()
    {
        if (_preparePhonesWindow is { } open)
        {
            BringBack(open);
            return open;
        }

        var window = new PreparePhonesWindow(
            _masterCertificate.Thumbprint, () => _tiles.Count(tile => tile.Machine.IsPhone), PhoneOnline, KickPhoneClaims)
        {
            Owner = this,
        };
        Action<KnownMachine> joined = machine => window.PhoneJoined(machine.Name);
        PhoneJoined += joined;
        window.Closed += (_, _) =>
        {
            PhoneJoined -= joined;
            _preparePhonesWindow = null;
        };
        _preparePhonesWindow = window;
        OwnedWindow.ReturnToOwnerWhenClosed(window);
        window.Show();
        return window;
    }

    // Null when the class has no phone with that name; else whether it answers now.
    private bool? PhoneOnline(string name) =>
        _tiles.FirstOrDefault(tile => tile.Machine.IsPhone && string.Equals(tile.Machine.Name, name, StringComparison.OrdinalIgnoreCase))
            is { } tile
            ? tile.Presence == TilePresence.Online
            : null;
}
