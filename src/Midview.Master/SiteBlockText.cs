namespace Midview.Master;

// What the sites window says, in the words of what it acts on: computers
// ("máquina"), phones ("celular") or both ("aparelho"). A phone blocks the
// sites in Chrome (its other browsers are hidden), and Chrome's entry for a
// site covers its other addresses too (m.youtube.com), which the hosts file
// of a PC does not; apps such as YouTube's keep opening, so the window says
// where to block them.
public static class SiteBlockText
{
    private const string StillRuns = "O Midview continua funcionando mesmo com a internet bloqueada.";
    private const string WithAndWithoutWww = "Bloqueia o endereço com e sem www (youtube.com e www.youtube.com).";

    private enum Devices
    {
        Computers,
        Phones,
        Both,
    }

    private static Devices Of(IReadOnlyCollection<KnownMachine> targets) =>
        !targets.Any(machine => machine.IsPhone) ? Devices.Computers
        : targets.All(machine => machine.IsPhone) ? Devices.Phones
        : Devices.Both;

    // The line at the top (TargetsText), counting celulares or aparelhos.
    public static string Targets(IReadOnlyList<KnownMachine> targets, bool wholeClass) =>
        TargetsText.Describe(TargetsPreposition.In, targets, wholeClass, Of(targets) switch
        {
            Devices.Phones => "celulares",
            Devices.Both => "aparelhos",
            _ => "máquinas",
        });

    public static string Persistence(IReadOnlyCollection<KnownMachine> targets) => Of(targets) switch
    {
        Devices.Phones => $"O bloqueio continua valendo se o celular reiniciar. {StillRuns}",
        Devices.Both => $"O bloqueio continua valendo se o aparelho reiniciar. {StillRuns}",
        _ => $"O bloqueio continua valendo se a máquina reiniciar. {StillRuns}",
    };

    // Under the site's buttons.
    public static string SiteHint(IReadOnlyCollection<KnownMachine> targets) => Of(targets) switch
    {
        Devices.Phones => $"{WithAndWithoutWww} Abas que já estavam abertas podem continuar funcionando.",
        Devices.Both => $"{WithAndWithoutWww} Abas que já estavam abertas podem continuar funcionando e, nos computadores, outros endereços do mesmo site, como m.youtube.com.",
        _ => $"{WithAndWithoutWww} Outros endereços do mesmo site, como m.youtube.com, e abas que já estavam abertas podem continuar funcionando.",
    };

    // Said when phones are among the targets; null when none is.
    public static string? PhoneNote(IReadOnlyCollection<KnownMachine> targets) =>
        targets.Count(machine => machine.IsPhone) switch
        {
            0 => null,
            1 => "No celular, o bloqueio de sites vale no Chrome; apps como o do YouTube continuam abrindo: bloqueie-os em \"Bloquear programas\".",
            _ => "Nos celulares, o bloqueio de sites vale no Chrome; apps como o do YouTube continuam abrindo: bloqueie-os em \"Bloquear programas\".",
        };

    // After a command went to every target.
    public static string Done(IReadOnlyCollection<KnownMachine> targets, int failures) =>
        failures > 0 ? $"Feito em {targets.Count - failures} de {targets.Count}."
        : targets.Count == 1 ? "Feito."
        : Of(targets) switch
        {
            Devices.Phones => $"Feito nos {targets.Count} celulares.",
            Devices.Both => $"Feito nos {targets.Count} aparelhos.",
            _ => $"Feito nas {targets.Count} máquinas.",
        };

    // The classroom list has no certificate for its agent.
    public static string NoThumbprint(KnownMachine machine) => machine.IsPhone
        ? "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\""
        : "a lista da sala não tem o certificado do Agent desta máquina: pareie-a de novo em \"Máquinas da sala\"";

    public static string Failure(KnownMachine machine, Exception ex) =>
        machine.IsPhone ? FailureText.DescribePhoneAgent(ex) : FailureText.DescribeAgent(ex);
}
