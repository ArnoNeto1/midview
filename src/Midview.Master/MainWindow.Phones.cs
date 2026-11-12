using System.Windows;

namespace Midview.Master;

// Optimizing and updating the phones: "Otimizar..." and "Atualizar o Midview
// do celular..." on a phone's tile menu, "Otimizar celulares e tablets..." in
// the "Telas" menu (MainWindow.Screens.cs; the selected Android devices, or
// every one of the room when none is selected), "Senha dos celulares e
// tablets..." next to it (the same devices) and "Atualizar os celulares" in
// "Máquinas da sala" (every phone of the room).
public partial class MainWindow
{
    private IReadOnlyList<KnownMachine> RoomPhones() =>
        _tiles.Select(tile => tile.Machine).Where(machine => machine.IsPhone).ToList();

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
            OpenPhoneUpdateWindow(PhonesSaying(MachinesOf(tile)));
        }
    }

    // The phones among a menu's machines; the status bar says the PCs among
    // them were left out.
    private IReadOnlyList<KnownMachine> PhonesSaying(IReadOnlyList<KnownMachine> targets)
    {
        if (DeviceSupport.PhoneOnlyLeftOutNotice(targets) is { } notice)
        {
            StatusTextBlock.Text = notice;
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

    // The APK of this Master's version, from GitHub, kept in
    // PhoneAgentApk.DefaultFolder; the client goes with the window.
    private void OpenPhoneUpdateWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            StatusTextBlock.Text = "Nenhum celular na lista da sala para atualizar.";
            return;
        }
        ShowOnce("atualizar-celulares", phones, () =>
        {
            var client = TeacherUpdate.CreateClient();
            var window = new PhoneUpdateWindow(
                phones, IsEveryPhone(phones), _masterCertificate,
                PhoneApkSource.GitHub(TeacherUpdate.CurrentVersion, client, PhoneAgentApk.DefaultFolder))
            {
                Owner = this,
            };
            window.Closed += (_, _) => client.Dispose();
            return window;
        });
    }
}
