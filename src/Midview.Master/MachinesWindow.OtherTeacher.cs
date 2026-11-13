using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Outro professor": "Permitir novo professor" draws a code, opens every
// machine of the room for 10 minutes to the teacher who has it, and shows it
// to be read out; "Esquecer os outros professores" leaves this teacher the
// only one on every machine. On the search page, the code a colleague read
// out is typed for that colleague's machines (NewTeacherInvite).
public partial class MachinesWindow
{
    private async void OnAllowNewTeacherClicked(object sender, RoutedEventArgs e)
    {
        AllowNewTeacherButton.IsEnabled = false;
        NewTeacherCodeCard.Visibility = Visibility.Collapsed;
        AllowNewTeacherResultTextBlock.Text = "Avisando as máquinas...";
        var code = NewTeacherInvite.NewCode();
        var payload = NewTeacherInvite.Encode(code);
        var results = await Task.WhenAll(_roster().Select(machine => Task.Run(() =>
        {
            var (succeeded, detail) = SendToAgent(machine, MessageType.AllowNewTeacher, payload);
            return (machine.Name, succeeded, detail);
        })));
        AllowNewTeacherResultTextBlock.Text = OtherTeacherText.AllowNewTeacherSummary(results);
        if (results.Any(result => result.succeeded))
        {
            NewTeacherCodeTextBlock.Text = NewTeacherInvite.Format(code);
            NewTeacherCodeCard.Visibility = Visibility.Visible;
        }
        AllowNewTeacherButton.IsEnabled = true;
    }

    private async void OnForgetOtherTeachersClicked(object sender, RoutedEventArgs e)
    {
        if (MidviewMessage.Show(this, OtherTeacherText.ForgetQuestion, "Esquecer os outros professores",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        ForgetOtherTeachersButton.IsEnabled = false;
        ForgetOtherTeachersResultTextBlock.Text = "Avisando as máquinas...";
        var results = await Task.WhenAll(_roster().Select(machine => Task.Run(() =>
        {
            var (succeeded, detail) = SendToAgent(machine, MessageType.ForgetOtherTeachers, Array.Empty<byte>());
            return (machine.Name, succeeded, detail);
        })));
        ForgetOtherTeachersResultTextBlock.Text = OtherTeacherText.ForgetSummary(results);
        ForgetOtherTeachersButton.IsEnabled = true;
    }

    // A command to a machine's Agent (a PC's or a phone's, same port), and
    // its answer as the summaries read it.
    private (bool Succeeded, string Detail) SendToAgent(KnownMachine machine, MessageType type, byte[] payload)
    {
        if (machine.AgentThumbprint is null)
        {
            return (false, "sem a identificação do Agent");
        }

        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, machine.AgentThumbprint, _masterCertificate, type, payload);
            return (succeeded, OtherTeacherText.Detail(succeeded, detail));
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribeAgent(ex));
        }
    }

    // The search page asks for the code only when it found a colleague's
    // machine taking one more teacher.
    private void ShowNewTeacherCodeField() =>
        NewTeacherCodePanel.Visibility = _rows.Any(row => row.NeedsCode) ? Visibility.Visible : Visibility.Collapsed;

    // The code typed for the chosen rows that need one, or null: then those
    // rows say what is missing and are not paired.
    private string? ChosenNewTeacherCode(IReadOnlyList<FoundRow> chosen)
    {
        var needing = chosen.Where(row => row.NeedsCode).ToList();
        if (needing.Count == 0)
        {
            return null;
        }
        if (NewTeacherInvite.TryRead(NewTeacherCodeTextBox.Text, out var code))
        {
            return code;
        }

        var text = NewTeacherCodeTextBox.Text.Trim().Length == 0 ? OtherTeacherText.NeedsCode : NewTeacherInvite.BadCode;
        foreach (var row in needing)
        {
            row.OutcomeIsError = true;
            row.Outcome = text;
        }
        NewTeacherCodeTextBox.Focus();
        return null;
    }
}
