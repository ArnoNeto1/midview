using Midview.Protocol;

namespace Midview.Master;

// What the two "Dar acesso a um colega" windows say.
public static class InviteText
{
    public static string Waiting(int machines, DateTime expiresAt) =>
        $"Vale por 10 minutos, uma vez (até {expiresAt:HH:mm}). Deixe esta janela aberta até o colega entrar. "
        + (machines == 1 ? "Vai liberar a máquina da sua sala." : $"Vai liberar as {machines} máquinas da sua sala.");

    // The teacher who gave the class: who got it.
    public static string Given(string teacherName, InviteResult result) =>
        result.Total == 0
            ? $"{teacherName} entrou, mas a sua lista da sala está vazia."
            : $"{teacherName} recebeu acesso a {result.Given.Count} de {result.Total} máquinas.{LeftOut(result)}";

    // The colleague: what came in.
    public static string Received(InviteResult result) =>
        result.Total == 0
            ? "A sala do colega não tem máquinas."
            : $"Pronto: {result.Given.Count} de {result.Total} máquinas na sua sala.{LeftOut(result)}";

    private static string LeftOut(InviteResult result) =>
        result.LeftOut.Count == 0
            ? string.Empty
            : $" Não liberadas: {string.Join("; ", result.LeftOut.Select(machine => $"{machine.Name} ({machine.Reason})"))}.";
}
