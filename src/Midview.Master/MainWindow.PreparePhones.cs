using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// "Preparar celulares" (PreparePhonesWindow): opened by the big button of the
// empty "Celulares" filter ("Preparar tablets" on the empty "Tablets") and by
// the "Configurar celular" and "Configurar tablet" pages of "Máquinas da sala".
public partial class MainWindow
{
    // The phone app is a few MB: a slow school connection still gets it.
    private static readonly HttpClient PhoneAppHttp = new() { Timeout = TimeSpan.FromMinutes(2) };

    // Google's adb, about 8 MB: no overall limit, AdbTool gives up on a
    // read that waits too long instead.
    private static readonly HttpClient AdbToolHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    private PreparePhonesWindow? _preparePhonesWindow;

    // The phones just prepared, searched for on the network until they
    // answer (or for 5 minutes), whether or not the assistant is still open.
    private readonly PhoneJoinWatch _phoneJoinWatch = new();
    private DispatcherTimer? _phoneJoinTimer;
    private bool _phoneJoinSearching;

    private void OnPreparePhonesClicked(object sender, RoutedEventArgs e) =>
        OpenPreparePhonesWindow((PreparedKind(), true));

    // What the big button of the empty filter prepares: tablets on "Tablets", phones otherwise.
    private AndroidDeviceKind PreparedKind() => DeviceSupport.AndroidKindOf(_devices) ?? AndroidDeviceKind.Phone;

    // One at a time: asking again brings the open one back. The window is
    // returned so a caller can start it at a number (StartAt). mode: phone or
    // tablet, and "Midview otimizado" or not ("Máquinas da sala"); null keeps
    // the open window's (StartAt sets its own). An open window asked for
    // another mode switches only when no device is named or on the cable in
    // it; otherwise it says so and goes on, rather than renaming or
    // preparing differently a device the teacher is in the middle of.
    internal PreparePhonesWindow OpenPreparePhonesWindow((AndroidDeviceKind Kind, bool Optimize)? mode = null)
    {
        if (_preparePhonesWindow is { } open)
        {
            BringBack(open);
            if (mode is { } asked && !open.SetMode(asked.Kind, asked.Optimize))
            {
                MidviewMessage.Show(open, open.ModeBusyText, open.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return open;
        }

        var window = new PreparePhonesWindow(new PreparePhonesHost(
            kind => PhoneNumber.CountOf(_tiles.Select(tile => tile.Machine), kind),
            PhoneOnline,
            AdbTool.ForThisComputer(AdbToolHttp),
            path => new AdbProcess(path),
            GetPhoneAppAsync,
            _masterCertificate.Thumbprint,
            AddPreparedPhone,
            IsPreparedPhoneOnline,
            WatchPreparedPhone,
            WipePhoneNamed),
            mode?.Kind ?? AndroidDeviceKind.Phone,
            mode?.Optimize ?? true)
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
        WatchPreparedPhone(phone);
    }

    private bool IsPreparedPhoneOnline(KnownMachine phone) =>
        phone.AgentThumbprint is { } thumbprint && IsPhoneOnline(thumbprint);

    private bool IsPhoneOnline(string agentThumbprint) =>
        _tiles.Any(tile => tile.Machine.IsPhone && tile.Presence == TilePresence.Online
                           && string.Equals(tile.Machine.AgentThumbprint, agentThumbprint, StringComparison.OrdinalIgnoreCase));

    // Searches the network for a phone just prepared (again for 5 minutes on
    // "Procurar de novo"): its tile was saved without an address, and only a
    // search gives it one. Runs on its own, with the assistant closed too.
    private void WatchPreparedPhone(KnownMachine phone)
    {
        if (phone.AgentThumbprint is not { } thumbprint)
        {
            return;
        }
        _phoneJoinWatch.Watch(thumbprint, DateTimeOffset.Now);
        if (_phoneJoinTimer is null)
        {
            _phoneJoinTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            _phoneJoinTimer.Tick += (_, _) => OnPhoneJoinTick();
            Closed += (_, _) => _phoneJoinTimer.Stop();
        }
        _phoneJoinTimer.Start();
        OnPhoneJoinTick();
    }

    // One search at a time, at most every 10 s, for all the phones waited for.
    private async void OnPhoneJoinTick()
    {
        var now = DateTimeOffset.Now;
        var due = _phoneJoinWatch.Tick(now, IsPhoneOnline);
        if (_phoneJoinWatch.IsEmpty)
        {
            _phoneJoinTimer?.Stop();
        }
        if (!due || _phoneJoinSearching)
        {
            return;
        }
        _phoneJoinSearching = true;
        _phoneJoinWatch.Searched(now);
        try
        {
            await SearchForPreparedPhoneAsync();
        }
        finally
        {
            _phoneJoinSearching = false;
        }
    }

    // A search of the network, as "Procurar máquinas na rede" does: the
    // phone prepared answers it from the address it got on the Wi-Fi.
    private async Task SearchForPreparedPhoneAsync()
    {
        var thumbprint = _masterCertificate.Thumbprint;
        try
        {
            var found = await Task.Run(() => DiscoveryClient.Search(
                thumbprint, WakeOnLanSender.DiscoveryTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime));
            await RefreshAddressesAsync(found);
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

    // The tile menu's "Apagar e preparar de novo...": only for one school
    // phone or tablet (named from a number; not one paired earlier by code),
    // and only once the phone setup is ready; active when it answers now.
    private static void ShowWipePhoneItem(ItemCollection items, IReadOnlyList<MachineTile> targets)
    {
        var item = items.OfType<MenuItem>().FirstOrDefault(item => DeviceActions.Of(item) == ToolbarAction.WipePhone);
        if (item is null)
        {
            return;
        }
        var phone = targets is [var only] && PhoneWipe.ShowsItem(only.Machine) ? only : null;
        item.Visibility = PhoneProvisioning.WizardReady && phone is not null ? Visibility.Visible : Visibility.Collapsed;
        item.IsEnabled = phone is not null && PhoneWipe.CanWipe(phone.Machine, phone.Presence == TilePresence.Online);
    }

    private async void OnWipePhoneMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile
            || !PhoneWipe.CanWipe(tile.Machine, tile.Presence == TilePresence.Online)
            || PhoneWipe.TargetOf(tile.Machine.Name) is not { } target
            || !PhoneWipe.Confirm(this, tile.Machine.Name))
        {
            return;
        }
        StatusTextBlock.Text = $"Apagando o {tile.Machine.Name}...";
        var (ok, detail) = await WipePhone(tile.Machine);
        if (!ok)
        {
            ShowWarning($"Não foi possível apagar o {tile.Machine.Name}: {detail}");
            return;
        }
        StatusTextBlock.Text = string.Empty;
        // A tablet is prepared again as a tablet ("Tablet 07"), a phone as a phone.
        OpenPreparePhonesWindow().StartAt(target.Number, target.Kind, wiped: true);
    }

    // The assistant's "Apagar e preparar de novo": the phone (or tablet) of the class by name.
    private Task<(bool Ok, string Detail)> WipePhoneNamed(string name) =>
        _tiles.FirstOrDefault(tile => tile.Machine.IsPhone && string.Equals(tile.Machine.Name, name, StringComparison.OrdinalIgnoreCase))
            is { } tile
            ? WipePhone(tile.Machine)
            : Task.FromResult((false, $"o {DeviceSetupText.Word(PhoneNumber.KindOf(name))} não está mais na turma"));

    private Task<(bool Ok, string Detail)> WipePhone(KnownMachine phone) =>
        Task.Run(() => PhoneWipe.Send(phone, _masterCertificate));
}
