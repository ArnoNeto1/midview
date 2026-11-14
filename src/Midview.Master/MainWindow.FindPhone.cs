using System.IO;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Tocar para encontrar" (PhoneFindText): from a phone's or tablet's tile
// menu (the machines it acts on) and from Telas (what the toolbar reaches).
// It rings every phone and tablet among them for a minute, or, while all of
// them still ring, stops them ("Parar o toque"); the status bar says how each
// one answered, an agent from before it included.
public partial class MainWindow
{
    // The tile menu's item (its Uid in MainWindow.xaml).
    internal const string FindPhoneUid = "tocar";

    private readonly PhoneFindRinging _ringing = new();

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

    private async void FindPhones(IReadOnlyList<KnownMachine> targets)
    {
        var phones = targets.Where(machine => machine.IsPhone).ToList();
        if (phones.Count == 0)
        {
            return;
        }
        var start = !_ringing.AllRinging(phones, DateTime.Now);
        var seconds = start ? PhoneFind.DefaultSeconds : PhoneFind.Stop;
        StatusTextBlock.Text = start ? "Fazendo tocar..." : "Parando o toque...";
        var answers = await Task.Run(() => phones.Select(phone =>
        {
            try
            {
                var (succeeded, detail) = PhoneFindClient.Send(phone, _masterCertificate, seconds);
                return (Phone: phone, Succeeded: succeeded, Line: PhoneFindText.Summary(phone.Name, start, succeeded, detail));
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException
                                           or System.Security.Authentication.AuthenticationException or TimeoutException)
            {
                return (Phone: phone, Succeeded: false, Line: PhoneFindText.Summary(phone.Name, start, false, ErrorText.Describe(ex)));
            }
        }).ToList());
        if (start)
        {
            _ringing.Started(answers.Where(answer => answer.Succeeded).Select(answer => answer.Phone), DateTime.Now, seconds);
        }
        else
        {
            _ringing.Stopped(phones);
        }
        StatusTextBlock.Text = WithNotice(string.Join(" ", answers.Select(answer => answer.Line)), DeviceSupport.PhoneOnlyLeftOutNotice(targets));
    }
}
