using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Tocar para encontrar" (PhoneFindText): from a phone's or tablet's tile
// menu (the machines it acts on) and from "Configurar celular" ("Configurar
// tablet") in "Máquinas da sala" (DeviceSupport.RoomTargets).
// It rings every phone and tablet among them for a minute, or, while all of
// them still ring, stops them ("Parar o toque"); the status bar says how each
// one answered, an agent from before it included.
public partial class MainWindow
{
    // The tile menu's item (its Uid in MainWindow.xaml).
    internal const string FindPhoneUid = "tocar";

    private readonly PhoneFindRinging _ringing = new();
    private readonly PhoneFindSender _findSender = new();

    private void OnFindPhoneMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            FindPhones(MachinesOf(tile));
        }
    }

    // What the menus call the item for these machines.
    internal string FindPhonesMenuItem(IReadOnlyList<KnownMachine> targets) =>
        PhoneFindText.MenuItem(_ringing.AllRinging(targets.Where(machine => machine.IsPhone).ToList(), DateTime.Now));

    // All devices at once (PhoneFindSender); a click while they are being
    // reached is ignored.
    private async void FindPhones(IReadOnlyList<KnownMachine> targets)
    {
        var phones = targets.Where(machine => machine.IsPhone).ToList();
        if (phones.Count == 0 || _findSender.Running)
        {
            return;
        }
        var start = !_ringing.AllRinging(phones, DateTime.Now);
        var seconds = start ? PhoneFind.DefaultSeconds : PhoneFind.Stop;
        StatusTextBlock.Text = start ? "Fazendo tocar..." : "Parando o toque...";
        IReadOnlyList<PhoneFindAnswer>? answers;
        try
        {
            answers = await _findSender.SendAsync(phones, start, phone => PhoneFindClient.Send(phone, _masterCertificate, seconds));
        }
        catch (Exception ex)
        {
            // Never the Master's end: only this click's.
            StatusTextBlock.Text = "Não deu para fazer tocar: " + FailureText.DescribePhoneAgent(ex);
            return;
        }
        if (answers is null)
        {
            return;
        }
        if (start)
        {
            _ringing.Started(answers.Where(answer => answer.Succeeded).Select(answer => answer.Phone), DateTime.Now, seconds);
        }
        else
        {
            _ringing.Stopped(phones);
        }
        StatusTextBlock.Text = WithNotice(string.Join(" ", answers.Select(answer => answer.Line)), DeviceSupport.PhoneOnlyLeftOutNotice(targets));
        _machinesWindow?.ShowRoomDevices(); // "Tocar..." or "Parar o toque"
    }
}
