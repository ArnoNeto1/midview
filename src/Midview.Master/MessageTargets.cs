using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Mensagem" with phones among the machines. A PC's Session shows it (port
// 5901, answered 1 or 0); a phone's agent (port 28900) gets the same
// ShowMessage and shows it over the student's app until "OK, ENTENDI", and
// answers why not when it can't, or that it waits for the teacher's lock
// screen to go (TeacherMessages on the phone). The sending lives here, so it
// can be tested.
public static class MessageTargets
{
    public const string NotShown = "a máquina não conseguiu mostrar a mensagem";

    public static readonly string OldAgent = PhoneAgentCommand.OldAgent("mostra mensagens");

    // What a phone says when the message is on its screen.
    private const string PhoneShown = "mensagem mostrada";

    // One machine's outcome: Failure null when it got the message; Note,
    // for a phone that got it, anything worth telling the teacher (it shows
    // once the teacher's lock screen goes).
    public sealed record Result(string Name, string? Failure, string? Note = null);

    // Blocks for the connection and the answer: call it off the UI thread.
    public static Result Send(KnownMachine machine, byte[] payload, X509Certificate2 masterCertificate)
    {
        if (machine.IsPhone)
        {
            return FromPhone(machine, PhoneAgentCommand.Send(machine, OldAgent, thumbprint => CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, thumbprint, masterCertificate, MessageType.ShowMessage, payload)));
        }
        try
        {
            var shown = CommandClient.SendCommand(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, masterCertificate,
                MessageType.ShowMessage, payload);
            return new Result(machine.Name, shown ? null : NotShown);
        }
        catch (Exception ex)
        {
            return new Result(machine.Name, FailureText.Describe(ex));
        }
    }

    // A phone's answer (PhoneAgentCommand.Send's) as the window lists it.
    internal static Result FromPhone(KnownMachine phone, (bool Succeeded, string Detail) answer) =>
        answer.Succeeded
            ? new Result(phone.Name, null, answer.Detail.Length == 0 || answer.Detail == PhoneShown ? null : answer.Detail)
            : new Result(phone.Name, answer.Detail.Length == 0 ? NotShown : answer.Detail);

    // Above the list, once every machine answered (or didn't).
    public static string Summary(IReadOnlyList<Result> results)
    {
        var failures = results.Where(result => result.Failure is not null).ToList();
        var delivered = results.Count - failures.Count;
        var text = failures.Count == 0
            ? results.Count == 1 ? "Mensagem enviada." : $"Mensagem enviada para as {delivered} máquinas."
            : $"Enviada para {delivered} de {results.Count}. Não chegou em:\n"
              + string.Join("\n", failures.Select(failure => $"• {failure.Name}: {failure.Failure}"));
        var notes = results.Where(result => result.Failure is null && result.Note is not null).ToList();
        return notes.Count == 0
            ? text
            : text + "\nObservações:\n" + string.Join("\n", notes.Select(note => $"• {note.Name}: {note.Note}"));
    }
}
