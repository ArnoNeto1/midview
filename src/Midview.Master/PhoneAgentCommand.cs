namespace Midview.Master;

// A command for a phone's agent (port 28900, the phone's own thumbprint), and
// what its answer becomes for the teacher: the agent's words as they come,
// except an agent from before the command, which answers "Ainda não funciona
// em celular." (or nothing), and is named as an older version.
public static class PhoneAgentCommand
{
    public const string NoIdentity = "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"";

    // What an agent from before a command answers to it.
    public const string NotOnPhone = "Ainda não funciona em celular.";

    // An older agent's answer, as the teacher reads it ("o Midview deste
    // celular é de uma versão anterior e ainda não fecha apps: atualize-o").
    public static string OldAgent(string notYet) =>
        $"o Midview deste celular é de uma versão anterior e ainda não {notYet}: atualize-o";

    // Whether the agent's answer is the one an agent from before the command gives.
    public static bool IsOldAgentAnswer(bool succeeded, string detail) =>
        !succeeded && (detail.Length == 0 || detail == NotOnPhone);

    // send: given the phone's agent thumbprint, the agent's answer. oldAgent:
    // what an older agent's answer becomes.
    public static (bool Succeeded, string Detail) Send(
        KnownMachine phone, string oldAgent, Func<string, (bool Succeeded, string Detail)> send)
    {
        if (phone.AgentThumbprint is not { Length: > 0 } thumbprint)
        {
            return (false, NoIdentity);
        }
        try
        {
            var (succeeded, detail) = send(thumbprint);
            return IsOldAgentAnswer(succeeded, detail) ? (false, oldAgent) : (succeeded, detail);
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribePhoneAgent(ex));
        }
    }
}
