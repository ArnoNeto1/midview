using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Desligar" and "Reiniciar" with phones among the machines. A PC's Agent
// (port 28900) shuts it down or restarts it after the warning the teacher
// picked. A phone's agent (the same port) gets the same command and warns
// the student with the same countdown, then restarts; but Android lets no
// app turn a phone off, so "Desligar" turns its screen off and locks it, and
// the phone stays on (PowerCountdown on the phone). Installing the Windows
// updates is for the PCs alone: a phone is sent the command without it.
// "Ligar" and "Agendar" stay for the PCs. The window's words and the sending
// live here, so they can be tested.
public static class PowerTargets
{
    public const string PcDelayHint =
        "O Windows mostra ao aluno um aviso com a contagem. Quando o tempo acaba, os programas abertos são fechados sem salvar. Vale também para máquinas sem ninguém logado.";

    public const string PhoneDelayHint =
        "O celular mostra ao aluno um aviso com a contagem. Quando o tempo acaba, ele reinicia ou apaga a tela.";

    public const string PhoneNote =
        "Nos celulares, \"Desligar\" apaga a tela e trava: o celular continua ligado (o Android não deixa o Midview desligá-lo). " +
        "\"Reiniciar\" reinicia o celular, e o Midview volta sozinho.";

    public const string MixedPhoneNote =
        "Nos celulares, \"Desligar\" apaga a tela e trava (o celular continua ligado) e \"Reiniciar\" reinicia; " +
        "o aluno também vê a contagem. Instalar as atualizações vale só para os computadores.";

    public static readonly string OldAgent = PhoneAgentCommand.OldAgent("reinicia nem apaga a tela");

    public const string NoAgentThumbprint =
        "falta o thumbprint do Agent (reinstale com o pacote de instalação, ou preencha em \"Testar / bloquear sites...\")";

    // Phones (and tablets) alone: the Windows updates do not apply.
    public static bool OnlyPhones(IReadOnlyCollection<KnownMachine> targets) => CloseAppsTargets.OnlyPhones(targets);

    // Under the warning's choices.
    public static string DelayHint(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets) ? PhoneDelayHint : PcDelayHint;

    // Under the action's choices, when there are phones; null otherwise.
    public static string? Note(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? PhoneNote : targets.Any(machine => machine.IsPhone) ? MixedPhoneNote : null;

    // Under the heading: whom it reaches ("Em 3 celulares: ...").
    public static string Describe(IReadOnlyList<KnownMachine> targets, bool wholeClass) =>
        CloseAppsTargets.Describe(targets, wholeClass);

    // Above the list, once every machine answered (or didn't).
    public static string Done(IReadOnlyCollection<KnownMachine> targets, int failures) =>
        CloseAppsTargets.Done(targets.Count, failures, OnlyPhones(targets));

    // What the machine is sent: a phone never the Windows updates' flag.
    public static byte[] Payload(KnownMachine machine, PowerAction action, int delaySeconds, bool installUpdates) =>
        PowerCommand.Encode(action, delaySeconds, installUpdates && !machine.IsPhone && action != PowerAction.Cancel);

    // The machine's line. Blocks for the connection and the answer: call it off the UI thread.
    public static (bool Succeeded, string Detail) Send(
        KnownMachine machine, PowerAction action, int delaySeconds, bool installUpdates, X509Certificate2 masterCertificate) =>
        Send(machine, action, delaySeconds, installUpdates, (thumbprint, payload) => CommandClient.SendCommandForResult(
            machine.HostAddress, ConnectionClient.AgentPort, thumbprint, masterCertificate, MessageType.Power, payload));

    // send: given the agent's thumbprint and the payload, its answer.
    internal static (bool Succeeded, string Detail) Send(
        KnownMachine machine, PowerAction action, int delaySeconds, bool installUpdates,
        Func<string, byte[], (bool Succeeded, string Detail)> send)
    {
        var payload = Payload(machine, action, delaySeconds, installUpdates);
        if (machine.IsPhone)
        {
            return PhoneAgentCommand.Send(machine, OldAgent, thumbprint => send(thumbprint, payload));
        }
        if (machine.AgentThumbprint is not { Length: > 0 } agentThumbprint)
        {
            return (false, NoAgentThumbprint);
        }
        try
        {
            var (succeeded, detail) = send(agentThumbprint, payload);
            return (succeeded, PowerText.Detail(succeeded, detail, installUpdates && action != PowerAction.Cancel));
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribeAgent(ex));
        }
    }
}
