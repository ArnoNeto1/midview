using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Midview.Protocol;

namespace Midview.Master;

// Sends "Abrir site" and "Abrir programa" to each machine's Session, all
// machines at once: an offline one must not hold up the rest. The same
// from the windows and from a click in the menu.
public static class RemoteLaunch
{
    // On a machine the sites open one after the other, in the order typed (so
    // the browser's tabs follow it); one that can't be reached isn't tried
    // again for each of the rest.
    public static Task<LaunchResult[]> OpenWebsites(
        IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate, IReadOnlyList<Uri> addresses) =>
        Task.WhenAll(targets.Select(machine => Task.Run(() =>
        {
            var answers = new List<(string Command, bool Succeeded, string Detail)>();
            foreach (var address in addresses)
            {
                var (succeeded, detail, reached) = Send(
                    machine, masterCertificate, MessageType.OpenWebsite, TextMessage.Encode(address.AbsoluteUri));
                if (!reached)
                {
                    return new LaunchResult(machine.Name, false, detail);
                }
                answers.Add((address.AbsoluteUri, succeeded, detail));
            }
            return LaunchText.Combine(machine.Name, answers);
        })));

    // On a machine the commands go one after the other; one that can't be
    // reached isn't tried again for each of the rest.
    public static Task<LaunchResult[]> OpenPrograms(
        IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate, IReadOnlyList<string> commands) =>
        Task.WhenAll(targets.Select(machine => Task.Run(() =>
        {
            var answers = new List<(string Command, bool Succeeded, string Detail)>();
            foreach (var command in commands)
            {
                var (succeeded, detail, reached) = Send(machine, masterCertificate, MessageType.OpenProgram, TextMessage.Encode(command));
                if (!reached)
                {
                    return new LaunchResult(machine.Name, false, detail);
                }
                answers.Add((command, succeeded, detail));
            }
            return LaunchText.Combine(machine.Name, answers);
        })));

    private static (bool Succeeded, string Detail, bool Reached) Send(
        KnownMachine machine, X509Certificate2 masterCertificate, MessageType type, byte[] payload)
    {
        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, masterCertificate, type, payload);
            return (succeeded, detail, true);
        }
        catch (Exception ex)
        {
            return (false, FailureText.Describe(ex), false);
        }
    }
}
