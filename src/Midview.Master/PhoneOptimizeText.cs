using Midview.Protocol;

namespace Midview.Master;

// How the "Otimizar" window words each phone's answer, kept apart so it can
// be tested. The DNS server's name is left out: it is a technical address.
public static class PhoneOptimizeText
{
    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não otimiza";

    // The device answers within 90 s: the apps, the DNS, then the home screen.
    public const string Working = "otimizando… (pode levar até um minuto e meio)";

    // One line per phone; null report: an older agent. One that sends no
    // uninstalled apps (before 0.0.74) only hides; one that sends no home
    // screen (or a state this Master does not know) says nothing about it.
    public static string Line(PhoneOptimizeReport? report)
    {
        if (report is null)
        {
            return OldAgent;
        }
        var line = $"{Apps(report.Uninstalled.Count, report.Hidden.Count, report.Failed.Count, report.PendingCount)} · {Dns(report.Dns)}";
        return Home(report.Home) is { } home ? $"{line} · {home}" : line;
    }

    // Done, but apps still leaving the phone in the background (0.0.75 on).
    public static bool IsRemoving(PhoneOptimizeReport? report) => report is { PendingCount: > 0 };

    // Done, but the home screen still being arranged in the background.
    public static bool IsArranging(PhoneOptimizeReport? report) => report is { Home: PhoneHomeStates.Working };

    // Fully optimized: every listed app uninstalled or hidden and the ad-free
    // DNS on (apps still being uninstalled in the background are not failed). With the
    // internet blocked the phone checks the DNS again by itself when it is
    // released, so that counts as done too; and so does a DNS the teacher
    // turned off in the lab panel. A home screen left for the teacher's
    // hands, or not arranged for a reason the teacher can fix (the screen
    // off, the accessibility off), is not done; one still being arranged, one
    // of another launcher (only the cable arranges it) or none reported is.
    public static bool IsDone(PhoneOptimizeReport? report) =>
        report is { Failed.Count: 0, Dns: PhoneDnsStates.On or PhoneDnsStates.OffInternetBlocked or PhoneDnsStates.OffByTeacher }
        && report.Home is not (PhoneHomeStates.ByHand or PhoneHomeStates.ScreenOff or PhoneHomeStates.NoAccessibility);

    // How the home screen was left; null when the device said nothing about
    // it (an older agent, not the school's device) or a state this Master
    // does not know.
    public static string? Home(string? state) => state switch
    {
        PhoneHomeStates.Arranged => "tela inicial arrumada",
        PhoneHomeStates.Working => "tela inicial sendo arrumada no aparelho",
        PhoneHomeStates.ByHand => "tela inicial: algo inesperado apareceu na tela, arrume o resto à mão",
        PhoneHomeStates.ScreenOff => "tela inicial não arrumada: a tela estava apagada ou bloqueada (acenda-a e otimize de novo)",
        PhoneHomeStates.NoAccessibility => "tela inicial não arrumada: a acessibilidade do Midview está desligada (ligue pelo cabo)",
        PhoneHomeStates.OtherLauncher => "tela inicial deste aparelho só é arrumada pelo cabo",
        _ => null,
    };

    // "28 apps desinstalados · 6 escondidos", and those Android refused
    // both ways (failed); pending: still leaving the phone in the background,
    // "13 apps desinstalados (mais 22 saindo no celular) · 21 escondidos".
    public static string Apps(int uninstalled, int hidden, int failed, int pending = 0)
    {
        if (pending > 0)
        {
            var removed = uninstalled == 0 ? "nenhum app desinstalado ainda" : Uninstalled(uninstalled);
            var leaving = $"{removed} (mais {pending} saindo no celular)";
            var withHidden = hidden switch
            {
                0 => leaving,
                1 => $"{leaving} · 1 escondido",
                _ => $"{leaving} · {hidden} escondidos",
            };
            return Refused(withHidden, failed);
        }

        var apps = (uninstalled, hidden, failed) switch
        {
            (0, 0, 0) => "nenhum app desnecessário neste celular",
            (0, 0, _) => "nenhum app desinstalado nem escondido",
            (0, 1, _) => "1 app escondido",
            (0, _, _) => $"{hidden} apps escondidos",
            (_, 0, _) => Uninstalled(uninstalled),
            (_, 1, _) => $"{Uninstalled(uninstalled)} · 1 escondido",
            _ => $"{Uninstalled(uninstalled)} · {hidden} escondidos",
        };
        return Refused(apps, failed);
    }

    private static string Refused(string apps, int failed) => failed switch
    {
        0 => apps,
        1 => $"{apps} (1 não pôde ser removido)",
        _ => $"{apps} ({failed} não puderam ser removidos)",
    };

    private static string Uninstalled(int count) => count == 1 ? "1 app desinstalado" : $"{count} apps desinstalados";

    public static string Dns(string state) => state switch
    {
        PhoneDnsStates.On => "DNS sem anúncios ligado",
        PhoneDnsStates.OffNetworkBlocks => "DNS sem anúncios desligado: a rede bloqueou",
        PhoneDnsStates.OffNoInternet => "DNS sem anúncios desligado: o celular estava sem internet",
        PhoneDnsStates.OffInternetBlocked => "DNS sem anúncios: conferido de novo quando a internet for liberada",
        PhoneDnsStates.OffByTeacher => "DNS sem anúncios desligado no Painel do laboratório",
        _ => "DNS sem anúncios desligado: o celular não conseguiu ligá-lo",
    };

    // Every Android device runs the same agent, phones and tablets alike.
    public static string Heading(int devices) => devices == 1 ? "Otimizar o celular ou tablet" : "Otimizar celulares e tablets";

    // Above the list, once every device answered (or didn't). removing and
    // arranging: how many of the done ones still uninstall apps, and still
    // arrange the home screen, in the background; while any does, it is not
    // "Pronto." yet.
    public static string Summary(int done, int total, int removing, int arranging)
    {
        if (arranging <= 0)
        {
            return Summary(done, total, removing);
        }
        if (total == 1)
        {
            return removing > 0
                ? "Quase pronto: o aparelho continua desinstalando os apps que faltam e arrumando a tela inicial."
                : "Quase pronto: o aparelho continua arrumando a tela inicial.";
        }
        var still = arranging == 1 ? "1 continua arrumando a tela inicial" : $"{arranging} continuam arrumando a tela inicial";
        if (removing > 0)
        {
            var leaving = removing == 1 ? "1 continua desinstalando os apps que faltam" : $"{removing} continuam desinstalando os apps que faltam";
            still = $"{leaving}; {still}";
        }
        return done == total
            ? $"Quase pronto nos {total} aparelhos: {still}."
            : $"{Summary(done, total)} Dos otimizados, {still}.";
    }

    public static string Summary(int done, int total, int removing)
    {
        if (removing <= 0)
        {
            return Summary(done, total);
        }
        if (total == 1)
        {
            return "Quase pronto: o celular continua desinstalando os apps que faltam.";
        }
        var still = removing == 1 ? "1 continua desinstalando" : $"{removing} continuam desinstalando";
        return done == total
            ? $"Quase pronto nos {total} aparelhos: {still} os apps que faltam."
            : $"{Summary(done, total)} Dos otimizados, {still} os apps que faltam.";
    }

    public static string Summary(int done, int total) => (done, total) switch
    {
        (1, 1) => "Pronto.",
        (_, 1) => "O aparelho não foi otimizado (veja abaixo).",
        _ when done == total => $"Pronto nos {total} aparelhos.",
        (1, _) => $"Otimizado 1 de {total} aparelhos (veja abaixo).",
        _ => $"Otimizados {done} de {total} aparelhos (veja abaixo).",
    };
}
