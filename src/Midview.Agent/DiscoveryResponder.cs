using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Agent;

// Answers the Master's "who's there?" (see Discovery): this machine's name
// and Agent thumbprint, and whom it trusts as the asking teacher sees it.
public sealed class DiscoveryResponder
{
    private readonly PairingService _pairing;
    private readonly string _agentThumbprint;
    private readonly string _machineName;

    public DiscoveryResponder(PairingService pairing, string agentThumbprint, string machineName)
    {
        _pairing = pairing;
        _agentThumbprint = agentThumbprint;
        _machineName = machineName;
    }

    public byte[]? Answer(byte[] packet) =>
        Discovery.ParseProbe(packet) is { } probe
            ? Discovery.Encode(new DiscoveryReply(
                probe.Id, _machineName, _agentThumbprint, _pairing.TeachersSeenBy(probe.MasterThumbprint), _pairing.NewTeacherOpen))
            : null;

    public async Task RunAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // A reset left by an earlier reply that couldn't be
                // delivered: the socket itself still works.
                continue;
            }

            if (Answer(received.Buffer) is not { } reply)
            {
                continue;
            }

            try
            {
                await udp.SendAsync(reply, received.RemoteEndPoint, cancellationToken);
            }
            catch (SocketException)
            {
                // The teacher's PC went away; nothing to do.
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
