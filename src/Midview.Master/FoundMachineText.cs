using Midview.Protocol;

namespace Midview.Master;

// What "Máquinas da sala" says about a search and the machines it found.
public static class FoundMachineText
{
    public const string InRoster = "Já está na sala";

    public static string Status(FoundMachine found, IReadOnlyCollection<KnownMachine> roster) =>
        IsInRoster(found, roster) ? InRoster
        : found.Teachers switch
        {
            TeacherStatus.None => "Sem professor",
            TeacherStatus.You => "Pareada com você",
            _ when found.NewTeacherOpen => "Com outro professor — aceitando novo professor",
            _ => "Com outro professor",
        };

    public static bool CheckedByDefault(FoundMachine found, IReadOnlyCollection<KnownMachine> roster) =>
        found.Teachers == TeacherStatus.None && !IsInRoster(found, roster);

    public static string SearchSummary(int found, int refreshed)
    {
        if (found == 0)
        {
            return "Nenhuma máquina respondeu. Confira se elas estão ligadas, com o Midview Aluno instalado e nesta mesma rede.";
        }

        var text = found == 1 ? "1 máquina encontrada." : $"{found} máquinas encontradas.";
        return refreshed switch
        {
            0 => text,
            1 => $"{text} 1 endereço atualizado na lista da sala.",
            _ => $"{text} {refreshed} endereços atualizados na lista da sala.",
        };
    }

    public static string AllowNewTeacherSummary(IReadOnlyList<(string Name, bool Opened, string Detail)> results)
    {
        if (results.Count == 0)
        {
            return "A lista da sala está vazia.";
        }

        var opened = results.Count(result => result.Opened);
        var text = $"{opened} de {results.Count} máquinas liberadas por 10 minutos.";
        var leftOut = results.Where(result => !result.Opened).Select(result => $"{result.Name} ({result.Detail})").ToList();
        return leftOut.Count == 0 ? text : $"{text} Não liberadas: {string.Join("; ", leftOut)}.";
    }

    private static bool IsInRoster(FoundMachine found, IReadOnlyCollection<KnownMachine> roster) =>
        roster.Any(entry => string.Equals(entry.AgentThumbprint, found.AgentThumbprint, StringComparison.OrdinalIgnoreCase));
}
