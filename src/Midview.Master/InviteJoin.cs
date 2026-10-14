using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// What the colleague should read: no teacher has that code open, the other
// teacher refused, or the connection dropped.
public sealed class InviteJoinException(string message) : Exception(message);

// Teacher B's side of "Dar acesso a um colega" (see TeacherInvite): find
// who has an invite open, find the one the code points to, send it the
// secret, and get its class.
public static class InviteJoin
{
    public const string NotFound =
        "Nenhum professor na rede tem esse código aberto. Confira o código com o colega e se o Midview Professor dele está aberto, com o código na tela.";
    public const string InvalidAnswer = "O outro professor respondeu de um jeito inválido. Peça outro código e tente de novo.";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // code: as TeacherInvite.TryRead gave it. probeTargets: the broadcasts
    // (WakeOnLanSender.BroadcastTargets with TeacherInvite.Port). progress
    // gets what to show while it works. Returns the computer name of the
    // teacher who gave the class, and the class.
    public static (string From, InviteResult Result) Join(
        string code, X509Certificate2 certificate, string teacherName,
        IReadOnlyList<IPEndPoint> probeTargets, TimeSpan listenTime, Action<string>? progress = null)
    {
        progress?.Invoke("Procurando o professor na rede...");
        foreach (var (endpoint, name) in Find(probeTargets, listenTime))
        {
            progress?.Invoke($"Conectando a {name}...");
            if (Connect(endpoint, certificate) is not { } connection)
            {
                continue;
            }

            using (connection)
            {
                // The code names one certificate: anyone else never hears
                // the secret.
                if (!TeacherInvite.PointsTo(code, connection.Channel.RemoteThumbprint))
                {
                    continue;
                }
                return (name, Ask(connection, code, teacherName, name, progress));
            }
        }
        throw new InviteJoinException(NotFound);
    }

    // Who answered "who has an invite open?": where to connect (the
    // answer's address, on the port it named) and their computer name. At
    // most TeacherInvite.MaxResponders, in the order they answered.
    public static IReadOnlyList<(IPEndPoint Endpoint, string Name)> Find(IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime)
    {
        var id = Guid.NewGuid();
        var packet = TeacherInvite.EncodeProbe(id);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        udp.Client.IOControl(DiscoveryClient.SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
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

        var found = new List<(IPEndPoint Endpoint, string Name)>();
        var deadline = DateTime.UtcNow + listenTime;
        while (found.Count < TeacherInvite.MaxResponders && deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            try
            {
                using var timeout = new CancellationTokenSource(left);
                var received = udp.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                if (TeacherInvite.ParseReply(received.Buffer) is { } reply && reply.ProbeId == id)
                {
                    var endpoint = new IPEndPoint(received.RemoteEndPoint.Address, reply.Port);
                    if (!found.Any(entry => entry.Endpoint.Equals(endpoint)))
                    {
                        found.Add((endpoint, reply.Name));
                    }
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
        return found;
    }

    // The colleague's machines, as this classroom list keeps them.
    public static IReadOnlyList<KnownMachine> ToKnownMachines(InviteResult result) =>
        result.Given
            .Select(machine => new KnownMachine(
                machine.Name, machine.HostAddress, machine.SessionThumbprint.ToUpperInvariant(),
                machine.AgentThumbprint.ToUpperInvariant(), WakeOnLan.NormalizeMacAddress(machine.MacAddress)))
            .ToList();

    private static Connection? Connect(IPEndPoint endpoint, X509Certificate2 certificate)
    {
        TcpClient? tcp = null;
        try
        {
            tcp = TcpConnect.WithTimeout(endpoint.Address.ToString(), endpoint.Port, ConnectTimeout);
            var timeout = (int)TeacherInvite.JoinTimeout.TotalMilliseconds;
            tcp.ReceiveTimeout = timeout;
            tcp.SendTimeout = timeout;
            return new Connection(tcp, SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, endpoint.Address.ToString(), certificate));
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or OperationCanceledException)
        {
            tcp?.Dispose();
            return null;
        }
    }

    private static InviteResult Ask(Connection connection, string code, string teacherName, string from, Action<string>? progress)
    {
        Message answer;
        try
        {
            connection.Channel.Send(new Message(
                MessageType.InviteJoin, new InviteJoinRequest(TeacherInvite.Secret(code), teacherName).Encode()));
            connection.Tcp.ReceiveTimeout = (int)TeacherInvite.DoneTimeout.TotalMilliseconds;
            progress?.Invoke($"{from} está liberando as máquinas...");
            answer = connection.Channel.Receive();
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
        {
            throw new InviteJoinException(
                $"A conexão com {from} caiu antes de a lista da sala chegar ({FailureText.DescribeAgent(ex)}). "
                + "Peça outro código ao colega: as máquinas que você já tem não se repetem.");
        }

        return answer.Type switch
        {
            MessageType.InviteDone => InviteResult.Decode(answer.Payload) ?? throw new InviteJoinException(InvalidAnswer),
            MessageType.InviteRefused => throw new InviteJoinException(TeacherInvite.RefusalText(answer.Payload)),
            _ => throw new InviteJoinException(InvalidAnswer),
        };
    }

    private sealed class Connection(TcpClient tcp, SecureChannel channel) : IDisposable
    {
        public TcpClient Tcp { get; } = tcp;

        public SecureChannel Channel { get; } = channel;

        public void Dispose()
        {
            Channel.Dispose();
            Tcp.Dispose();
        }
    }
}
