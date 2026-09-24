using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Midview.Master;

// What went wrong reaching a machine, in the teacher's words. The usual
// cases get the classroom grid's short statuses ("Offline", "Sem usuário
// logado"...) instead of Windows' or .NET's own message, which can be long
// or even in English ("The operation was canceled."). Anything else keeps
// the technical message, which is what helps with something unexpected.
public static class FailureText
{
    // For connections to a Session (port 5901).
    public static string Describe(Exception ex) => Known(ex, refused: "Sem usuário logado") ?? Technical(ex);

    // For the Agent's commands (port 28900): the Agent runs whether or not
    // anyone is logged in, so a refused connection means it isn't running.
    public static string DescribeAgent(Exception ex) =>
        Known(ex, refused: "O Midview não está rodando nessa máquina") ?? Technical(ex);

    // The grid's tile status: always one of the short ones.
    public static string Status(Exception ex) => Known(ex, refused: "Sem usuário logado") ?? "Sem resposta";

    private static string? Known(Exception ex, string refused) => ex switch
    {
        // The machine is on, but nothing listens on the port: for the
        // Session, usually nobody is logged in (it only runs inside a login).
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => refused,
        // A computer name (see "Conectar pelo nome do computador") that the
        // network couldn't resolve.
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }
            => "Nome não encontrado na rede",
        // Switched off or unreachable: the connect timed out (5 s).
        SocketException or OperationCanceledException => "Offline",
        // Either side didn't trust the other's certificate: a wrong
        // thumbprint here, or this Master missing from the machine's
        // trusted-masters.json.
        AuthenticationException => "Certificado recusado",
        // Connected, but the machine stopped answering or hung up.
        IOException => "Sem resposta",
        _ => null,
    };

    // The inner exception usually carries the actual reason (e.g. which
    // certificate check failed), so it's shown alongside the outer message.
    private static string Technical(Exception ex) =>
        ex.InnerException is not null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
}
