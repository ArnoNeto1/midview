namespace Midview.Master;

// What "Agendar" (ScheduleWindow) says about the machines it reaches. Phones
// and tablets take part in turning off only: their screen goes off and locks
// (no app can turn an Android device off), and nothing turns one on, so the
// wake part stays the computers' (WakeTask).
public static class ScheduleText
{
    public const string AndroidHint = "Nos celulares e tablets, \"Desligar\" apaga e trava a tela; ligar não é possível.";

    // What an Android agent from before "Agendar" answers, as the teacher reads it.
    public static readonly string OldPhone = PhoneAgentCommand.OldAgent("apaga a tela no horário agendado");

    private const string OldPc = "o Midview dessa máquina é de uma versão anterior: reinstale com o pacote atual";
    private const string NoAgentThumbprint =
        "falta o thumbprint do Agent (reinstale com o pacote de instalação, ou preencha em \"Testar / bloquear sites...\")";

    // "Na turma toda (25 máquinas)"; with phones or tablets among them, each
    // kind counted: "Na turma toda (25 máquinas: 20 computadores, 3 celulares
    // e 2 tablets)", or "Na turma toda (3 celulares)" when there is one kind.
    public static string Targets(IReadOnlyCollection<KnownMachine> machines)
    {
        var all = Count(machines.Count, "máquina", "máquinas");
        if (!DeviceSupport.HasAndroid(machines))
        {
            return $"Na turma toda ({all})";
        }

        var computers = machines.Count(machine => !DeviceSupport.IsAndroid(machine));
        var phones = machines.Count(machine => DeviceSupport.IsAndroidOf(machine, AndroidDeviceKind.Phone));
        var tablets = machines.Count(machine => DeviceSupport.IsAndroidOf(machine, AndroidDeviceKind.Tablet));
        var kinds = new[]
            {
                (computers, Count(computers, "computador", "computadores")),
                (phones, Count(phones, "celular", "celulares")),
                (tablets, Count(tablets, "tablet", "tablets")),
            }
            .Where(kind => kind.Item1 > 0)
            .Select(kind => kind.Item2)
            .ToList();
        var listed = kinds.Count == 1 ? kinds[0] : $"{string.Join(", ", kinds.Take(kinds.Count - 1))} e {kinds[^1]}";
        return kinds.Count == 1 ? $"Na turma toda ({listed})" : $"Na turma toda ({all}: {listed})";
    }

    // The "Ligar" line after saving, and whether it counts for "Agendamento
    // salvo." (a failure in it does). Without computers among the machines,
    // a time to turn on is nothing to report as done or failed.
    public static (bool Succeeded, string Line) WakeLine(
        bool succeeded, string detail, bool wakeScheduled, IReadOnlyCollection<KnownMachine> machines)
    {
        if (wakeScheduled && machines.Count > 0 && machines.All(DeviceSupport.IsAndroid))
        {
            return (true, "• Ligar: só para computadores, e esta turma não tem nenhum (celulares e tablets não ligam pelo Midview)");
        }
        return (succeeded, $"{(succeeded ? "✓" : "✗")} Ligar: {detail}");
    }

    // Sends the schedule to one machine's agent (send: given its agent's
    // thumbprint, the answer) and gives what its line says: the agent's words,
    // or why it did not take it. A machine that is off gets it later from its
    // tile's poller.
    public static (bool Succeeded, string Detail) Send(KnownMachine machine, Func<string, (bool Succeeded, string Detail)> send)
    {
        if (machine.AgentThumbprint is not { Length: > 0 } thumbprint)
        {
            return (false, machine.IsPhone ? PhoneAgentCommand.NoIdentity : NoAgentThumbprint);
        }

        try
        {
            var (succeeded, detail) = send(thumbprint);
            if (machine.IsPhone && PhoneAgentCommand.IsOldAgentAnswer(succeeded, detail))
            {
                return (false, OldPhone);
            }
            // A PC Agent from before scheduling answers a bare "no".
            return (succeeded, !succeeded && detail.Length == 0 ? OldPc : detail);
        }
        catch (Exception ex)
        {
            return (false, machine.IsPhone
                ? $"{FailureText.DescribePhoneAgent(ex)}; ele recebe o agendamento quando o MidviewMaster o vir ligado"
                : $"{FailureText.DescribeAgent(ex)}; ela recebe o agendamento quando o MidviewMaster a vir ligada");
        }
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}
