using System.IO;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Optimizing and updating the phones: "Otimizar..." and "Atualizar o Midview
// do celular..." on a phone's tile menu, the cards of "Configurar celular"
// and "Configurar tablet" in "Máquinas da sala" (ActOnRoomDevices: the
// selected devices of that kind, or every one of the room when none is
// selected) and "Atualizar os celulares" / "Atualizar os tablets" there too
// (every phone, or every tablet, of the room).
public partial class MainWindow
{
    private IReadOnlyList<KnownMachine> RoomPhones(AndroidDeviceKind kind) =>
        _tiles.Select(tile => tile.Machine).Where(machine => DeviceSupport.IsAndroidOf(machine, kind)).ToList();

    private const string ConfigureDevicesUid = "configurar";

    // "Configurar tablet..." of a tile's menu: the "Configurar tablet" page of
    // "Máquinas da sala", acting only on these devices (DeviceRoomSection).
    private void OnConfigureDevicesClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }
        var (kind, devices) = DevicesToConfigure(tile);
        if (devices.Count == 0)
        {
            return;
        }
        OpenMachinesWindow(search: false);
        _machinesWindow?.ShowDevices(kind, devices);
    }

    // The kind of the tile clicked (of the first phone or tablet among the
    // menu's machines, if it is a PC), and the menu's devices of that kind.
    private (AndroidDeviceKind Kind, IReadOnlyList<KnownMachine> Devices) DevicesToConfigure(MachineTile tile)
    {
        var machines = MachinesOf(tile);
        var first = tile.Machine.IsPhone ? tile.Machine : machines.FirstOrDefault(machine => machine.IsPhone);
        var kind = first is null ? AndroidDeviceKind.Phone : PhoneNumber.KindOf(first.Name);
        return (kind, DeviceSupport.RoomTargets(kind, machines));
    }

    // A card of "Configurar celular" ("Configurar tablet"): its window on the
    // devices DeviceSupport.RoomTargets gave.
    private void ActOnRoomDevices(DeviceRoomAction action, IReadOnlyList<KnownMachine> devices)
    {
        switch (action)
        {
            case DeviceRoomAction.Optimize:
                OpenOptimizeWindow(devices);
                break;
            case DeviceRoomAction.Pin:
                OpenPhonePinWindow(devices);
                break;
            case DeviceRoomAction.Network:
                OpenPhoneNetworkWindow(devices);
                break;
            case DeviceRoomAction.Accounts:
                OpenPhoneAccountsWindow(devices, PhoneAccountsCard.Accounts);
                break;
            case DeviceRoomAction.ClearChrome:
                OpenPhoneAccountsWindow(devices, PhoneAccountsCard.ClearChrome);
                break;
            case DeviceRoomAction.Debugging:
                OpenPhoneAccountsWindow(devices, PhoneAccountsCard.Debugging);
                break;
            case DeviceRoomAction.Wallpaper:
                OpenPhoneWallpaperWindow(devices);
                break;
            case DeviceRoomAction.Find:
                FindPhones(devices);
                break;
            case DeviceRoomAction.Update:
                OpenPhoneUpdateWindow(devices, PhoneUpdateText.KindOf(devices));
                break;
            case DeviceRoomAction.Wipe when devices is [var one]:
                WipeAndPrepareAgain(one);
                break;
            case DeviceRoomAction.Remove:
                OpenPhoneRemoveWindow(devices);
                break;
        }
    }

    private bool IsEveryPhone(IReadOnlyList<KnownMachine> phones) =>
        phones.Count == _tiles.Count(tile => tile.Machine.IsPhone);

    private void OpenOptimizeWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = "Nenhum celular ou tablet na lista da sala para otimizar.";
            return;
        }
        ShowOnce("otimizar", phones, () => new PhoneOptimizeWindow(phones, IsEveryPhone(phones), _masterCertificate) { Owner = this });
    }

    // The PIN is typed in the window each time: the Master never keeps it.
    private void OpenPhonePinWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = "Nenhum celular ou tablet na lista da sala para pôr o PIN.";
            return;
        }
        ShowOnce("pin-celulares", phones, () => new PhonePinWindow(phones, IsEveryPhone(phones), _masterCertificate) { Owner = this });
    }

    // "Liberar Wi-Fi e modo avião...": the same devices as the PIN.
    private void OpenPhoneNetworkWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = "Nenhum celular ou tablet na lista da sala para liberar o Wi-Fi.";
            return;
        }
        ShowOnce("rede-celulares", phones, () => new PhoneNetworkWindow(phones, IsEveryPhone(phones), _masterCertificate) { Owner = this });
    }

    // "Contas do aparelho", "Depuração USB" and "Sair das contas do Chrome": the same devices as the PIN.
    private void OpenPhoneAccountsWindow(IReadOnlyList<KnownMachine> phones, PhoneAccountsCard card)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = card switch
            {
                PhoneAccountsCard.ClearChrome => "Nenhum celular ou tablet na lista da sala para sair das contas do Chrome.",
                PhoneAccountsCard.Debugging => "Nenhum celular ou tablet na lista da sala para liberar a depuração USB.",
                _ => "Nenhum celular ou tablet na lista da sala para liberar as contas.",
            };
            return;
        }
        var key = card switch
        {
            PhoneAccountsCard.ClearChrome => "chrome-celulares",
            PhoneAccountsCard.Debugging => "depuracao-celulares",
            _ => "contas-celulares",
        };
        ShowOnce(key, phones, () => new PhoneAccountsWindow(phones, IsEveryPhone(phones), _masterCertificate, card) { Owner = this });
    }

    // "Papel de parede dos celulares e tablets...": the same devices as the PIN.
    private void OpenPhoneWallpaperWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = "Nenhum celular ou tablet na lista da sala para trocar o papel de parede.";
            return;
        }
        ShowOnce("papel-de-parede-celulares", phones, () => new PhoneWallpaperWindow(phones, IsEveryPhone(phones), _masterCertificate) { Owner = this });
    }

    // "Tirar o Midview dos celulares e tablets...": the same devices as the
    // PIN; each one the Midview left goes out of the room's list.
    private void OpenPhoneRemoveWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = "Nenhum celular ou tablet na lista da sala para tirar o Midview.";
            return;
        }
        ShowOnce("tirar-midview-celulares", phones,
            () => new PhoneRemoveWindow(phones, IsEveryPhone(phones), _masterCertificate, RemoveFromRoom) { Owner = this });
    }

    // A device the Midview left (or that is being erased) leaves the room's
    // list, as "Remover da sala" does.
    private void RemoveFromRoom(KnownMachine machine)
    {
        var index = _tiles.ToList().FindIndex(tile => tile.Machine.IsSameEntryAs(machine)
            || (tile.Machine.IsPhone && machine.AgentThumbprint is { Length: > 0 } agent
                && string.Equals(tile.Machine.AgentThumbprint, agent, StringComparison.OrdinalIgnoreCase)));
        if (index < 0)
        {
            return;
        }
        RemoveTileAt(index);
        UpdateEmptyHint();
        try
        {
            SaveRoster();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning($"Removido da grade, mas a lista não pôde ser gravada em disco: {ErrorText.Describe(ex)}");
        }
    }

    // The APK of this Master's version, from GitHub, kept in
    // PhoneAgentApk.DefaultFolder; the client goes with the window. kind
    // words the window (PhoneUpdateText.KindOf for a menu's devices); phones
    // and tablets get the same APK.
    private void OpenPhoneUpdateWindow(IReadOnlyList<KnownMachine> phones, AndroidDeviceKind kind)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = PhoneUpdateText.NoneToUpdate(kind);
            return;
        }
        var everyOne = phones.All(phone => DeviceSupport.IsAndroidOf(phone, kind)) && phones.Count == RoomPhones(kind).Count;
        ShowOnce(kind == AndroidDeviceKind.Tablet ? "atualizar-tablets" : "atualizar-celulares", phones, () =>
        {
            var client = TeacherUpdate.CreateClient();
            var window = new PhoneUpdateWindow(
                phones, everyOne, _masterCertificate,
                PhoneApkSource.GitHub(TeacherUpdate.CurrentVersion, client, PhoneAgentApk.DefaultFolder), kind)
            {
                Owner = this,
            };
            window.Closed += (_, _) => client.Dispose();
            return window;
        });
    }
}
