using Midview.Protocol;

namespace Midview.Master;

// The "Painel do laboratório" with phones in the room. A phone takes the
// functions that mean something on it, through the same commands as a PC
// (SetLabConfig, GetLabConfig, ResetLabConfig, BlockDomain):
// - "Bloqueador de anúncios": the ad-free DNS of "Otimizar", on or back to
//   automatic. The panel is the one place that decides it: "Otimizar" then
//   hides apps and leaves the DNS as the panel says.
// - "Travar downloads": Chrome's "block all downloads" policy (Chrome 131 or
//   newer); what the teacher sends with "Enviar arquivo" still arrives.
// - "Bloquear sites de distração": Chrome's site block, as "Bloquear sites".
// - "RESET geral": downloads free and the ad-free DNS on (a phone's default).
// - "Ver o que está aplicado".
// Every other function is for computers only: never sent to a phone, and
// said so in the window when there are phones among the targets. Some of
// them a phone already has for good (its locks as the school's phone).
public static class LabConfigPhones
{
    // The function names, as the button Tags of LabConfigWindow.xaml
    // (LabConfigChange.With) and the other rows of the window.
    public const string Distraction = "distraction";
    public const string ClearProfiles = "clearProfiles";
    public const string ClearDownloads = "clearDownloads";
    public const string Reset = "reset";
    public const string ShowApplied = "show";

    // Under the window's heading when there are phones among the targets.
    public const string PhonesHint =
        "Há celulares nesta lista: o que cada função faz neles está em rosa. No celular o Chrome não fecha; a mudança vale na hora.";

    // How long a phone may take to answer a SetLabConfig or a reset: turning
    // the ad-free DNS off waits for an optimization under way (~36 s).
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> OnPhones = new() { "adblock", "downloads", Distraction, Reset, ShowApplied };

    public static bool ReachesPhones(string function) => OnPhones.Contains(function);

    // Who a function reaches among the window's targets: every one, or the
    // computers alone.
    public static IReadOnlyList<KnownMachine> Reach(string function, IReadOnlyList<KnownMachine> targets) =>
        ReachesPhones(function) ? targets : targets.Where(machine => !DeviceSupport.IsAndroid(machine)).ToList();

    // Said under a row when phones are among the targets; null for a row that
    // needs no word about them.
    public static string? Note(string function) => function switch
    {
        "adblock" => "Nos celulares: liga o DNS sem anúncios (o mesmo do \"Otimizar\"); \"Desativar\" volta o DNS ao automático, e o \"Otimizar\" respeita.",
        "downloads" => "Nos celulares: o Chrome não baixa nenhum arquivo (precisa do Chrome 131 ou mais novo). O que você manda com \"Enviar arquivo\" continua chegando.",
        Distraction => "Nos celulares também, pelo Chrome.",
        Reset => "Nos celulares: libera os downloads e liga de novo o DNS sem anúncios (o padrão do celular).",
        "clean" => "Só nos computadores: o Chrome do celular não tem essa opção.",
        "lockdown" => "Só nos computadores. Os celulares já ficam sempre travados pelo Midview: o aluno não muda o DNS, a VPN nem as contas, não instala apps de fora e não restaura o celular.",
        "edge" => "Só nos computadores. Nos celulares, os outros navegadores já ficam sempre escondidos: só o Chrome abre sites.",
        "guest" => "Só nos computadores. Nos celulares, o aluno já não consegue entrar com uma conta.",
        "taskbar" or "desktop" or ClearProfiles or ClearDownloads => "Só nos computadores.",
        _ => null,
    };

    // After a computers-only function: the phones it left out.
    public static string? LeftOut(int phones) => phones switch
    {
        0 => null,
        1 => "O celular ficou de fora: essa função é só dos computadores.",
        _ => $"Os {phones} celulares ficaram de fora: essa função é só dos computadores.",
    };

    // A phone's agent from before the panel came to phones.
    public static readonly string OldAgent = PhoneAgentCommand.OldAgent("usa o Painel do laboratório");

    // "Ver o que está aplicado" for a phone, which has only its two functions.
    public static string Describe(LabConfig config)
    {
        var on = new List<string>();
        if (config.BlockDownloads) on.Add("travar downloads");
        if (config.Adblock) on.Add("bloqueador de anúncios (DNS)");
        return on.Count == 0 ? "nada aplicado" : string.Join(", ", on);
    }
}
