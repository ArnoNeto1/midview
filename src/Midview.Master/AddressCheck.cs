using System.Security.Cryptography.X509Certificates;

namespace Midview.Master;

// What it takes for a search to give a machine of the list a new address.
// A Discovery answer is only a hint: anyone on the network can send one (a
// student's program too), and the first answer wins. So a machine moves only
// once its own Agent proves itself at the new address -- a full handshake
// against the thumbprint the list pins, as every command to it makes -- and
// no longer at the old one (a PC with two network cards answering from the
// other one stays put).
public static class AddressCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Whether the Agent with this thumbprint answers at address (and trusts
    // this Master). Any failure means no.
    public static bool AgentAnswers(string address, string agentThumbprint, X509Certificate2 masterCertificate,
        int port = ConnectionClient.AgentPort, TimeSpan? timeout = null)
    {
        try
        {
            using var connection = ConnectionClient.Connect(address, port, agentThumbprint, masterCertificate, timeout ?? Timeout);
            return true;
        }
        catch (Exception)
        {
            // Refused, timed out, another certificate, a reset: not proven.
            return false;
        }
    }

    // A machine the Master's own search looks for: one a search can move
    // (Roster.MovesBySearch) that isn't answering at its address, or whose
    // address now answers as something else -- a Session that doesn't trust
    // this Master ("Certificado recusado": maybe another student's PC took the
    // address) or nothing listening ("Sem usuário logado": maybe another device).
    public static bool MayHaveMoved(KnownMachine machine, TilePresence presence, string status) =>
        Roster.MovesBySearch(machine) && (presence == TilePresence.Offline || FailureText.MayBeAnotherDevice(status));

    // The changes (Roster.NewAddresses, with the entries they replace) whose
    // new address holds, checked side by side. agentAnswers(address,
    // thumbprint) is AgentAnswers with this Master's certificate.
    public static async Task<IReadOnlyList<(KnownMachine Old, KnownMachine Updated)>> Proven(
        IReadOnlyList<(KnownMachine Old, KnownMachine Updated)> changes, Func<string, string, bool> agentAnswers)
    {
        var proofs = await Task.WhenAll(changes.Select(change => Task.Run(() =>
        {
            var agent = change.Updated.AgentThumbprint!;
            return agentAnswers(change.Updated.HostAddress, agent) && !agentAnswers(change.Old.HostAddress, agent);
        })));
        return changes.Where((_, i) => proofs[i]).ToList();
    }
}
