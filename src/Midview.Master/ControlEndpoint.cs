namespace Midview.Master;

// Which of a machine's two listeners "Controlar" talks to: the Desk helper
// (which can follow the screen onto the UAC prompt) or the Session (which
// can't, but every machine has one).
public enum ControlEndpoint
{
    Helper,
    Session,
}

public static class ControlChoice
{
    // The helper when the machine has one paired (an AgentThumbprint) and it
    // answered; the Session otherwise, which is also what a machine from
    // before the helper gets. A phone has no helper: its screen port takes
    // control itself.
    public static (ControlEndpoint Endpoint, int Port, string Thumbprint) Choose(
        KnownMachine machine, bool helperReachable)
    {
        if (helperReachable && !machine.IsPhone && !string.IsNullOrEmpty(machine.AgentThumbprint))
        {
            return (ControlEndpoint.Helper, ConnectionClient.DeskPort, machine.AgentThumbprint);
        }

        return (ControlEndpoint.Session, ConnectionClient.SessionPort, machine.SessionThumbprint);
    }
}
