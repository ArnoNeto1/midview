using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Fechar programas" with phones among the machines. A PC's Session closes its
// programs (port 5901), asking or forcing, after the warning the teacher
// picked. A phone's agent (port 28900) gets the same command: it warns the
// student for the same time, with a countdown, then closes every app the
// student opened and shows the home screen (CloseCountdown and AppCloser on
// the phone); asking or forcing has no meaning there, the apps close. The
// teacher's block, lock screen, internet block and PIN stay. The window's
// words and the sending live here, so they can be tested.
public static class CloseAppsTargets
{
    public const string PcHint =
        "Fecha os programas abertos pelo aluno. A área de trabalho, a barra de tarefas e o Midview nunca são fechados.";

    public const string PhoneHint =
        "Avisa o aluno e, no fim da espera, fecha todos os apps que ele abriu e volta para a tela inicial. O Midview, o telefone, as configurações e os bloqueios do professor continuam.";

    public const string PhoneNote =
        "Nos celulares, o aluno também é avisado e os apps fecham no fim da espera. Pedir para fechar ou forçar vale só para os computadores: no celular, os apps sempre fecham.";

    public static readonly string OldAgent = PhoneAgentCommand.OldAgent("fecha apps");

    public const string NoIdentity = PhoneAgentCommand.NoIdentity;

    // Phones (and tablets) alone: asking or forcing does not apply (the wait does).
    public static bool OnlyPhones(IReadOnlyCollection<KnownMachine> targets) =>
        targets.Count > 0 && targets.All(machine => machine.IsPhone);

    // Above the choices.
    public static string Hint(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets) ? PhoneHint : PcHint;

    // Under the choices, when phones go along with PCs; null otherwise.
    public static string? Note(IReadOnlyCollection<KnownMachine> targets) =>
        !OnlyPhones(targets) && targets.Any(machine => machine.IsPhone) ? PhoneNote : null;

    public static string Button(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets) ? "Fechar apps" : "Fechar programas";

    // While the machines are being asked.
    public static string Progress(int delaySeconds) => delaySeconds == 0 ? "Fechando..." : "Avisando os alunos...";

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
    internal static (bool Succeeded, string Detail) SendToPhone(KnownMachine phone, Func<string, (bool Succeeded, string Detail)> send) =>
        PhoneAgentCommand.Send(phone, OldAgent, send);
}
