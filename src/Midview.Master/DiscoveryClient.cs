using System.Net;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// A machine that answered "Procurar na rede". Address is where the answer
// came from.
public sealed record FoundMachine(string Name, string Address, string AgentThumbprint, string Teachers, bool NewTeacherOpen);

// The Master's side of Discovery: one probe to every target (the
// broadcasts, see WakeOnLanSender.BroadcastTargets), then the answers that
// arrive within listenTime.
public static class DiscoveryClient
{
    // Keeps an unreachable target from ending the listening early: without
    // it, Windows fails the next receive after a "port unreachable".
    private const int SioUdpConnReset = -1744830452;

    public static IReadOnlyList<FoundMachine> Search(string masterThumbprint, IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime)
    {
        var probe = new DiscoveryProbe(Guid.NewGuid(), masterThumbprint);
        var packet = Discovery.Encode(probe);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        foreach (var target in targets)
        {
            try
            {
                udp.Send(packet, packet.Length, target);
            }
            catch (SocketException)
            {
                // A card that just went down: the other targets still go.
            }
        }

        var answers = new List<(IPAddress, DiscoveryReply)>();
        var deadline = DateTime.UtcNow + listenTime;
        while (deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            try
            {
                using var timeout = new CancellationTokenSource(left);
                var received = udp.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                if (Discovery.ParseReply(received.Buffer) is { } reply)
                {
                    answers.Add((received.RemoteEndPoint.Address, reply));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Keep listening.
            }
        }
        return Merge(probe.Id, answers);
    }

    // One entry per machine (by Agent; a machine with two cards answers
    // twice, and the first answer wins), in classroom order. Answers to
    // someone else's probe are dropped.
    public static IReadOnlyList<FoundMachine> Merge(Guid probeId, IEnumerable<(IPAddress From, DiscoveryReply Reply)> answers) =>
        answers
            .Where(answer => answer.Reply.ProbeId == probeId)
            .GroupBy(answer => answer.Reply.AgentThumbprint.ToUpperInvariant())
            .Select(group => group.First())
            .Select(answer => new FoundMachine(
                answer.Reply.Name, answer.From.ToString(), answer.Reply.AgentThumbprint.ToUpperInvariant(),
                answer.Reply.Teachers, answer.Reply.NewTeacherOpen))
            .OrderBy(machine => machine.Name, NaturalOrder.Instance)
            .ToList();
}
