using System.Windows;

namespace Midview.Master;

// The CELULARES group ("Otimizar", "Atualizar celulares") and the same two
// items on a phone's tile menu. The group shows only in a room with a phone;
// it acts on the selected phones, or on every phone of the room when none is
// selected (DeviceSupport.ToolbarReach).
public partial class MainWindow
{
    private void ShowPhoneGroup()
    {
        PhoneGroupPanel.Visibility = DeviceSupport.ShowsPhoneGroup(_tiles.Select(tile => tile.Machine))
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateActionButtons();
    }

    private void OnOptimizePhonesClicked(object sender, RoutedEventArgs e) =>
        OpenOptimizeWindow(ToolbarReach(ToolbarAction.Optimize));

    private void OnUpdatePhonesClicked(object sender, RoutedEventArgs e) =>
        OpenPhoneUpdateWindow(ToolbarReach(ToolbarAction.UpdatePhones));

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
            StatusTextBlock.Text = "Nenhum celular na lista da sala para otimizar.";
            return;
        }
        ShowOnce("otimizar", phones, () => new PhoneOptimizeWindow(phones, IsEveryPhone(phones), _masterCertificate) { Owner = this });
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
