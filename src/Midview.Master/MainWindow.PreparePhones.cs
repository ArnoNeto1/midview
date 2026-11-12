using System.Windows;
using System.Windows.Controls;

namespace Midview.Master;

// "Preparar celulares" (PreparePhonesWindow): opened by the big button of the
// empty "Celulares" filter and by the link in "Máquinas da sala".
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
            () => _tiles.Count(tile => tile.Machine.IsPhone), PhoneOnline, WipePhoneNamed)
        {
            Owner = this,
        };
        window.Closed += (_, _) => _preparePhonesWindow = null;
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

    // The tile menu's "Apagar e preparar de novo...": only for one phone, and
    // only once the phone setup is ready; active when the phone answers now.
    private static void ShowWipePhoneItem(ItemCollection items, IReadOnlyList<MachineTile> targets)
    {
        var item = items.OfType<MenuItem>().FirstOrDefault(item => DeviceActions.Of(item) == ToolbarAction.WipePhone);
        if (item is null)
        {
            return;
        }
        var phone = targets is [{ Machine.IsPhone: true } only] ? only : null;
        item.Visibility = PhoneProvisioning.WizardReady && phone is not null ? Visibility.Visible : Visibility.Collapsed;
        item.IsEnabled = phone is not null && PhoneWipe.CanWipe(phone.Machine, phone.Presence == TilePresence.Online);
    }

    private async void OnWipePhoneMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile
            || !PhoneWipe.CanWipe(tile.Machine, tile.Presence == TilePresence.Online)
            || PhoneWipe.NumberOf(tile.Machine.Name) is not { } number
            || !PhoneWipe.Confirm(this, tile.Machine.Name))
        {
            return;
        }
        StatusTextBlock.Text = $"Apagando o {tile.Machine.Name}...";
        var (ok, detail) = await WipePhone(tile.Machine);
        if (!ok)
        {
            StatusTextBlock.Text = $"Não foi possível apagar o {tile.Machine.Name}: {detail}";
            return;
        }
        StatusTextBlock.Text = string.Empty;
        OpenPreparePhonesWindow().StartAt(number, wiped: true);
    }

    // The assistant's "Apagar e preparar de novo": the phone of the class by name.
    private Task<(bool Ok, string Detail)> WipePhoneNamed(string name) =>
        _tiles.FirstOrDefault(tile => tile.Machine.IsPhone && string.Equals(tile.Machine.Name, name, StringComparison.OrdinalIgnoreCase))
            is { } tile
            ? WipePhone(tile.Machine)
            : Task.FromResult((false, "o celular não está mais na turma"));

    private Task<(bool Ok, string Detail)> WipePhone(KnownMachine phone) =>
        Task.Run(() => PhoneWipe.Send(phone, _masterCertificate));
}
