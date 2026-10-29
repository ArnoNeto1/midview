using System.Net;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// A machine that answered "Procurar na rede". Address is where the answer
// came from.
public sealed record FoundMachine(string Name, string Address, string AgentThumbprint, string Teachers, bool NewTeacherOpen);

// The Master's side of Discovery: one probe to every target (broadcasts and
// local subnet hosts, see WakeOnLanSender.DiscoveryTargets), then the answers
// that arrive within listenTime.
public static class DiscoveryClient
{
    // Keeps an unreachable target from ending the listening early: without
    // it, Windows fails the next receive after a "port unreachable".
    internal const int SioUdpConnReset = -1744830452;

    // At most this many answers are read once listenTime is over. A busy PC
    // may only get to them then, but they arrived in time. The cap keeps
    // junk that never stops coming from holding the listening open.
    internal const int MaxLateAnswers = 1000;

    public static IReadOnlyList<FoundMachine> Search(string masterThumbprint, IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        udp.Client.ReceiveBufferSize = 1 << 20;
        udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        return Search(udp, new DiscoveryProbe(Guid.NewGuid(), masterThumbprint), targets, listenTime);
    }

    // udp: where the answers arrive. The tests queue answers there first.
    internal static IReadOnlyList<FoundMachine> Search(UdpClient udp, DiscoveryProbe probe, IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime)
    {
        var packet = Discovery.Encode(probe);
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
        Listen(udp, listenTime, received =>
        {
            if (Discovery.ParseReply(received.Buffer) is { } reply)
            {
                answers.Add((received.RemoteEndPoint.Address, reply));
            }
            return true;
        });
        return Merge(probe.Id, answers);
    }

    // Hands take every packet that arrives within listenTime, then the ones
    // already waiting when it is over (at most MaxLateAnswers), until take
    // says it has enough.
    internal static void Listen(UdpClient udp, TimeSpan listenTime, Func<UdpReceiveResult, bool> take)
    {
        var deadline = DateTime.UtcNow + listenTime;
        while (deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            try
            {
                using var timeout = new CancellationTokenSource(left);
                if (!take(udp.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult()))
                {
                    return;
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

        for (var late = 0; late < MaxLateAnswers && udp.Available > 0; late++)
        {
            try
            {
                IPEndPoint? from = null;
                var buffer = udp.Receive(ref from);
                if (!take(new UdpReceiveResult(buffer, from!)))
                {
                    return;
                }
            }
            catch (SocketException)
            {
                // Keep reading what is waiting.
            }
        }
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
