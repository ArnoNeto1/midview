using Midview.Protocol;

namespace Midview.Master;

// What the two "Dar acesso a um colega" windows say.
public static class InviteText
{
    public static string Waiting(int machines, DateTime expiresAt) =>
        $"Vale por 10 minutos, uma vez (até {expiresAt:HH:mm}). Deixe esta janela aberta até o colega entrar. "
        + (machines == 1 ? "Vai liberar a máquina da sua sala." : $"Vai liberar as {machines} máquinas da sua sala.");

    // The teacher who gave the class, once the invite is over.
    public static string Ended(InviteOutcome outcome) => outcome.End switch
    {
        InviteEnd.Given when !outcome.ListSent =>
            $"{Given(outcome.TeacherName!, outcome.Result!)} A conexão caiu antes de a lista chegar ao colega: gere outro código para ele (as máquinas não se repetem).",
        InviteEnd.Given => Given(outcome.TeacherName!, outcome.Result!),
        InviteEnd.WrongCode => "Alguém na rede tentou entrar com um código errado. O convite foi cancelado. Gere outro se ainda precisar.",
        InviteEnd.Expired => "O código expirou. Gere outro se ainda precisar.",
        _ => "O convite foi cancelado.",
    };

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
