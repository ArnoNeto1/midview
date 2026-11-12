using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// "Preparar celulares" (PreparePhonesWindow): opened by the big button of the
// empty "Celulares" filter and by the link in "Máquinas da sala".
public partial class MainWindow
{
    // The phone app is a few MB: a slow school connection still gets it.
    private static readonly HttpClient PhoneAppHttp = new() { Timeout = TimeSpan.FromMinutes(2) };

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

        var window = new PreparePhonesWindow(new PreparePhonesHost(
            () => _tiles.Count(tile => tile.Machine.IsPhone),
            PhoneOnline,
            () => new AdbProcess(),
            GetPhoneAppAsync,
            _masterCertificate.Thumbprint,
            AddPreparedPhone,
            IsPreparedPhoneOnline,
            SearchForPreparedPhoneAsync,
            WipePhoneNamed))
        {
            Owner = this,
        };
        window.Closed += (_, _) => _preparePhonesWindow = null;
        _preparePhonesWindow = window;
        OwnedWindow.ReturnToOwnerWhenClosed(window);
        window.Show();
        return window;
    }

    private static async Task<(AgentApkResult Result, string ApkPath)> GetPhoneAppAsync(CancellationToken token)
    {
        var apk = new AgentApk(PhoneAppHttp, MasterData.Folder, PhoneProvisioning.SignatureChecksum);
        return (await apk.GetAsync(token), apk.ApkPath);
    }

    // A phone the assistant prepared over the cable: in the class (a phone
    // with the same name, the same one prepared again, is replaced), saved,
    // and shown off until a search of the network finds its address.
    private void AddPreparedPhone(KnownMachine phone)
    {
        var problem = PhoneJoinRoster.AddAndSave(() =>
        {
            var kept = PhoneJoinRoster.ReplacingSameName(_tiles.Select(tile => tile.Machine).ToList(), phone);
            for (var i = _tiles.Count - 1; i >= 0; i--)
            {
                if (!kept.Any(machine => ReferenceEquals(machine, _tiles[i].Machine)))
                {
                    RemoveTileAt(i);
                }
            }
            InsertTile(_tiles.Count, phone);
            UpdateEmptyHint();
            SaveRoster();
        });
        if (problem is not null)
        {
            StatusTextBlock.Text = problem;
        }
    }

    private bool IsPreparedPhoneOnline(KnownMachine phone) =>
        _tiles.Any(tile => tile.Machine.IsPhone && tile.Presence == TilePresence.Online
                           && string.Equals(tile.Machine.AgentThumbprint, phone.AgentThumbprint, StringComparison.OrdinalIgnoreCase));

    // A search of the network, as "Procurar máquinas na rede" does: the
    // phone prepared answers it from the address it got on the Wi-Fi.
    private async Task SearchForPreparedPhoneAsync()
    {
        var thumbprint = _masterCertificate.Thumbprint;
        try
        {
            var found = await Task.Run(() => DiscoveryClient.Search(
                thumbprint, WakeOnLanSender.DiscoveryTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime));
            RefreshAddresses(found);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            // The next search, 10 s later, tries again.
        }
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
