using System.IO;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Optimizing and updating the phones: "Otimizar..." and "Atualizar o Midview
// do celular..." on a phone's tile menu, "Otimizar celulares e tablets..." in
// the "Telas" menu (MainWindow.Screens.cs; the selected Android devices, or
// every one of the room when none is selected), "Senha dos celulares e
// tablets..." next to it (the same devices) and "Atualizar os celulares" /
// "Atualizar os tablets" in "Máquinas da sala" (every phone, or every tablet,
// of the room).
public partial class MainWindow
{
    private IReadOnlyList<KnownMachine> RoomPhones(AndroidDeviceKind kind) =>
        _tiles.Select(tile => tile.Machine).Where(machine => DeviceSupport.IsAndroidOf(machine, kind)).ToList();

    private void OnOptimizeMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenOptimizeWindow(PhonesSaying(MachinesOf(tile)));
        }
    }

    private void OnUpdatePhonesMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            var phones = PhonesSaying(MachinesOf(tile));
            OpenPhoneUpdateWindow(phones, PhoneUpdateText.KindOf(phones));
        }
    }

    // The phones among a menu's machines; the status bar says the PCs among
    // them were left out.
    private IReadOnlyList<KnownMachine> PhonesSaying(IReadOnlyList<KnownMachine> targets)
    {
        if (DeviceSupport.PhoneOnlyLeftOutNotice(targets) is { } notice)
        {
            ShowNotice(notice);
        }
        return targets.Where(machine => machine.IsPhone).ToList();
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
