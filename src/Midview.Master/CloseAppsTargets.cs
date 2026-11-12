using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Fechar programas" with phones among the machines. A PC's Session closes its
// programs (port 5901), asking or forcing, after the warning the teacher
// picked. A phone's agent (port 28900) gets the same command and closes every
// app the student opened at once, with no warning and no question, and shows
// the home screen (AppCloser on the phone): the teacher's block, lock screen,
// internet block and PIN stay. The window's words and the sending live here,
// so they can be tested.
public static class CloseAppsTargets
{
    public const string PcHint =
        "Fecha os programas abertos pelo aluno. A área de trabalho, a barra de tarefas e o Midview nunca são fechados.";

    public const string PhoneHint =
        "Fecha todos os apps abertos pelo aluno, na hora, e volta para a tela inicial. O Midview, o telefone, as configurações e os bloqueios do professor continuam.";

    public const string PhoneNote =
        "Nos celulares, os apps fecham na hora, sem aviso e sem perguntar: o pedido e a espera valem só para os computadores.";

    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não fecha apps: atualize-o";

    public const string NoIdentity = "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"";

    // What an agent from before this answers to a command it doesn't know.
    private const string NotOnPhone = "Ainda não funciona em celular.";

    // Phones (and tablets) alone: the choices of a PC's closing do not apply.
    public static bool OnlyPhones(IReadOnlyCollection<KnownMachine> targets) =>
        targets.Count > 0 && targets.All(machine => machine.IsPhone);

    // Above the choices.
    public static string Hint(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets) ? PhoneHint : PcHint;

    // Under the choices, when phones go along with PCs; null otherwise.
    public static string? Note(IReadOnlyCollection<KnownMachine> targets) =>
        !OnlyPhones(targets) && targets.Any(machine => machine.IsPhone) ? PhoneNote : null;

    public static string Button(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets) ? "Fechar apps" : "Fechar programas";

    // While the machines are being asked.
    public static string Progress(IReadOnlyCollection<KnownMachine> targets, int delaySeconds) =>
        OnlyPhones(targets) || delaySeconds == 0 ? "Fechando..." : "Avisando os alunos...";

    // Under the heading: whom it closes ("Em 3 celulares: ...").
    public static string Describe(IReadOnlyList<KnownMachine> targets, bool wholeClass) =>
        TargetsText.Describe(TargetsPreposition.In, targets, wholeClass, OnlyPhones(targets) ? "celulares" : "máquinas");

    // Above the list, once every machine answered (or didn't).
    public static string Done(int total, int failures, bool onlyPhones = false) => failures == 0
        ? total == 1 ? "Concluído." : onlyPhones ? $"Concluído nos {total} celulares." : $"Concluído nas {total} máquinas."
        : $"Concluído em {total - failures} de {total}.";

    // Blocks for the connection and the answer: call it off the UI thread.
    public static (bool Succeeded, string Detail) Send(KnownMachine machine, byte[] payload, X509Certificate2 masterCertificate)
    {
        if (machine.IsPhone)
        {
            return SendToPhone(machine, thumbprint => CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, thumbprint, masterCertificate, MessageType.CloseApps, payload));
        }
        try
        {
            return CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, masterCertificate,
                MessageType.CloseApps, payload);
        }
        catch (Exception ex)
        {
            return (false, FailureText.Describe(ex));
        }
    }

    // send: given the phone's agent thumbprint, the agent's answer.
    internal static (bool Succeeded, string Detail) SendToPhone(KnownMachine phone, Func<string, (bool Succeeded, string Detail)> send)
    {
        if (phone.AgentThumbprint is not { Length: > 0 } thumbprint)
        {
            return (false, NoIdentity);
        }
        try
        {
            var (succeeded, detail) = send(thumbprint);
            return !succeeded && (detail.Length == 0 || detail == NotOnPhone) ? (false, OldAgent) : (succeeded, detail);
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribePhoneAgent(ex));
        }
    }
}
