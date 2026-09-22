namespace Midview.Master;

internal static class MidviewPorts
{
    public const int Session = 5901; // must match the TcpListener in Midview.Session's Program.cs
    public const int Agent = 28900; // must match Midview.Agent.AgentServerWorker.AgentPort
}
