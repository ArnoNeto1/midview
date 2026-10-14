using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Dar acesso a um colega", on the machines: TrustTeacher to every machine
// in the classroom list at once (as "Permitir novo professor" does), and
// what the colleague gets back: the machines that took it, as this list has
// them, and why the others didn't.
public static class ClassGrant
{
    public const string NoAgentIdentity = "sem a identificação do Agent: pareie-a de novo";
    public const string OldAgent = "Midview antigo: atualize-a";

    // tell sends TrustTeacher to one machine (OverTheNetwork; a fake in
    // tests). Blocks until every machine answered or failed.
    public static InviteResult Give(IReadOnlyList<KnownMachine> roster, Func<KnownMachine, (bool Succeeded, string Detail)> tell)
    {
        var answers = Task.WhenAll(roster.Select(machine => Task.Run(() => Answer(machine, tell)))).GetAwaiter().GetResult();
        var given = new List<InvitedMachine>();
        var leftOut = new List<LeftOutMachine>();
        for (var i = 0; i < roster.Count; i++)
        {
            var machine = roster[i];
            var (succeeded, detail) = answers[i];
            if (succeeded)
            {
                given.Add(new InvitedMachine(
                    machine.Name, machine.HostAddress, machine.SessionThumbprint.Trim().ToUpperInvariant(),
                    machine.AgentThumbprint!.Trim().ToUpperInvariant(), machine.MacAddress));
            }
            else
            {
                leftOut.Add(new LeftOutMachine(machine.Name, detail));
            }
        }
        return new InviteResult(given, leftOut);
    }

    // TrustTeacher over the network, pinned to each machine's Agent.
    public static Func<KnownMachine, (bool Succeeded, string Detail)> OverTheNetwork(X509Certificate2 masterCertificate, TeacherGrant grant)
    {
        var payload = grant.Encode();
        return machine => CommandClient.SendCommandForResult(
            machine.HostAddress, ConnectionClient.AgentPort, machine.AgentThumbprint!, masterCertificate,
            MessageType.TrustTeacher, payload);
    }

    // Only a machine the colleague could use is told: both thumbprints there
    // to pin. An Agent from before TrustTeacher answers with an empty
    // CommandResult.
    private static (bool Succeeded, string Detail) Answer(KnownMachine machine, Func<KnownMachine, (bool Succeeded, string Detail)> tell)
    {
        if (!TeacherInvite.IsThumbprint(machine.AgentThumbprint?.Trim()) || !TeacherInvite.IsThumbprint(machine.SessionThumbprint.Trim()))
        {
            return (false, NoAgentIdentity);
        }

        try
        {
            var (succeeded, detail) = tell(machine);
            return (succeeded, succeeded || detail.Length > 0 ? detail : OldAgent);
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribeAgent(ex));
        }
    }
}
