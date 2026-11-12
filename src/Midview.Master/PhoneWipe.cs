using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Midview.Protocol;

namespace Midview.Master;

// "Apagar e preparar de novo": the Master asks a school phone's Agent to erase
// the phone (back to factory settings), and the assistant then goes on at
// turning it on ("ligar"), with the same number, to prepare it again over the
// cable. The words and the sending live here, apart from the windows that use
// them.
public static class PhoneWipe
{
    // What a phone with an Agent from before this command answers: an empty
    // CommandAck, or the "not on a phone" one every unknown command gets.
    private const string NotOnPhone = "Ainda não funciona em celular.";

    public const string OldAgent = "este celular ainda não aceita o pedido de apagar";

    public static string ConfirmText(string name) =>
        $"O {name} vai ser apagado e voltar de fábrica. Os alunos perdem o que estiver nele. Para prepará-lo de novo, vai precisar outra vez do chip e da conta Mi.";

    // "Sim, apagar" or "Cancelar"; cancelling is the default.
    public static bool Confirm(System.Windows.Window? owner, string name) =>
        QuestionWindow.Ask(owner, new QuestionContent(
            "Apagar e preparar de novo", MidviewMessage.IconFor(System.Windows.MessageBoxImage.Warning),
            "Apagar e preparar de novo", "Sim, apagar", "Cancelar", Body: ConfirmText(name), NoIsDefault: true,
            Buttons: QuestionButtons.YesFirstDanger));

    // "07" for "Celular 07"; null for a name that is not a school phone's.
    public static string? NumberOf(string name)
    {
        var match = Regex.Match(name ?? "", @"^Celular (\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    // The menu item works on a school phone (named from a number) that
    // answers now.
    public static bool CanWipe(KnownMachine machine, bool online) =>
        machine.IsPhone && online && NumberOf(machine.Name) is not null;

    // The menu item is shown only for a school phone (named from a number):
    // a phone paired earlier by code ("Celular 549C") could never use it.
    public static bool ShowsItem(KnownMachine machine) =>
        machine.IsPhone && NumberOf(machine.Name) is not null;

    // Blocks for the connection and the answer: call it off the UI thread.
    public static (bool Ok, string Detail) Send(KnownMachine phone, X509Certificate2 cert) =>
        SendWith(phone, thumbprint => CommandClient.SendCommandForResult(
            phone.HostAddress, ConnectionClient.AgentPort, thumbprint, cert, MessageType.WipePhone, Array.Empty<byte>()));

    // send: given the phone's Agent thumbprint, the result of the command.
    internal static (bool Ok, string Detail) SendWith(KnownMachine phone, Func<string, (bool Succeeded, string Detail)> send)
    {
        if (phone.AgentThumbprint is not { } thumbprint)
        {
            return (false, "sem a identificação do celular");
        }

        try
        {
            var (succeeded, detail) = send(thumbprint);
            if (succeeded)
            {
                return (true, "");
            }
            return (false, detail.Length == 0 || detail == NotOnPhone ? OldAgent : detail);
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribeAgent(ex));
        }
    }
}
