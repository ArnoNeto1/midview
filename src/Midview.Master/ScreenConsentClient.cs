using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Pedir de novo para ver a tela": asks a phone's agent (port 28900) to show
// the student the "Iniciar agora" request again. The phone answers CommandAck:
// "pedido mostrado", or why not (the screen is locked, say).
public static class ScreenConsentClient
{
    public const string MenuText = "Pedir de novo para ver a tela";

    // What the phone says when it did put the request on the screen.
    public const string RequestShown = "pedido mostrado";

    // What it says when the teacher already sees its screen: nothing to ask.
    public const string AlreadyAllowed = "a tela já está liberada";

    public static (bool Succeeded, string Detail) Ask(
        KnownMachine phone, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            phone.HostAddress, port, AgentThumbprint(phone), masterCertificate, MessageType.RequestScreenConsent, Array.Empty<byte>());

    // One line for the status bar (a tablet's name says it is a tablet).
    public static string Summary(string phoneName, bool succeeded, string detail) =>
        succeeded && detail == RequestShown ? $"{phoneName}: pedido mostrado na tela, o aluno toca em \"Iniciar agora\"."
        : succeeded && detail == AlreadyAllowed ? $"{phoneName}: a tela já aparece no Midview, não precisa pedir de novo."
        : $"{phoneName}: {detail}";

    private static string AgentThumbprint(KnownMachine machine) =>
        machine.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do Agent");
}
