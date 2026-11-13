using Midview.Protocol;

namespace Midview.Master;

// What the "Outro professor" page and the search page say about letting
// another teacher in ("Permitir novo professor", with its code) and about
// "Esquecer os outros professores".
public static class OtherTeacherText
{
    public const string OldAgent = "Midview antigo: atualize-a";

    public const string ForgetQuestion =
        "Os outros professores deixam de controlar as máquinas desta sala. Continuar?";

    public const string NeedsCode =
        "Digite o código que apareceu na tela do colega em \"Permitir novo professor\".";

    // An answer from an Agent from before the command: a PC's bare no, or a
    // phone's "Ainda não funciona em celular.".
    public static string Detail(bool succeeded, string detail) =>
        PhoneAgentCommand.IsOldAgentAnswer(succeeded, detail) ? OldAgent : detail;

    public static string AllowNewTeacherSummary(IReadOnlyList<(string Name, bool Opened, string Detail)> results)
    {
        if (results.Count == 0)
        {
            return "A lista da sala está vazia.";
        }

        var opened = results.Count(result => result.Opened);
        var text = $"{opened} de {results.Count} máquinas liberadas por 10 minutos.";
        var withoutCode = results
            .Where(result => result.Opened && !NewTeacherInvite.AsksForCode(result.Detail))
            .Select(result => result.Name)
            .ToList();
        if (withoutCode.Count > 0)
        {
            text += $" Com Midview antigo, aceitam qualquer professor até lá, sem o código: {string.Join(", ", withoutCode)}. Atualize-as.";
        }
        return text + LeftOut(results.Where(result => !result.Opened).Select(result => (result.Name, result.Detail)), "Não liberadas");
    }

    // Each machine says how many teachers it forgot ("1 professor esquecido")
    // or that it had no other ("nenhum outro professor").
    public static string ForgetSummary(IReadOnlyList<(string Name, bool Kept, string Detail)> results)
    {
        if (results.Count == 0)
        {
            return "A lista da sala está vazia.";
        }

        var kept = results.Where(result => result.Kept).ToList();
        var text = $"{kept.Count} de {results.Count} máquinas agora só aceitam você.";
        var forgot = kept
            .Where(result => result.Detail != OtherTeachers.NoneForgotten)
            .Select(result => $"{result.Name} ({result.Detail})")
            .ToList();
        if (forgot.Count > 0)
        {
            text += $" Esquecidos: {string.Join("; ", forgot)}.";
        }
        return text + LeftOut(results.Where(result => !result.Kept).Select(result => (result.Name, result.Detail)), "Não alteradas");
    }

    private static string LeftOut(IEnumerable<(string Name, string Detail)> machines, string heading)
    {
        var listed = machines.Select(machine => $"{machine.Name} ({machine.Detail})").ToList();
        return listed.Count == 0 ? string.Empty : $" {heading}: {string.Join("; ", listed)}.";
    }
}
