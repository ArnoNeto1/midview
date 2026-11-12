using Midview.Protocol;

namespace Midview.Master;

// How the "Otimizar" window words each phone's answer, kept apart so it can
// be tested. The DNS server's name is left out: it is a technical address.
public static class PhoneOptimizeText
{
    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não otimiza";

    public const string Working = "otimizando… (pode levar até um minuto)";

    // One line per phone; null report: an older agent.
    public static string Line(PhoneOptimizeReport? report) =>
        report is null ? OldAgent : $"{Apps(report.Hidden.Count, report.Failed.Count)} · {Dns(report.Dns)}";

    // Fully optimized: every ad app hidden and the ad-free DNS on. With the
    // internet blocked the phone checks the DNS again by itself when it is
    // released, so that counts as done too; and so does a DNS the teacher
    // turned off in the lab panel.
    public static bool IsDone(PhoneOptimizeReport? report) =>
        report is { Failed.Count: 0, Dns: PhoneDnsStates.On or PhoneDnsStates.OffInternetBlocked or PhoneDnsStates.OffByTeacher };

    public static string Apps(int hidden, int failed)
    {
        var apps = (hidden, failed) switch
        {
            (0, 0) => "nenhum app de anúncios neste celular",
            (0, _) => "nenhum app de anúncios escondido",
            (1, _) => "1 app de anúncios escondido",
            _ => $"{hidden} apps de anúncios escondidos",
        };
        return failed switch
        {
            0 => apps,
            1 => $"{apps} (1 não pôde ser escondido)",
            _ => $"{apps} ({failed} não puderam ser escondidos)",
        };
    }

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

    // Above the list, once every device answered (or didn't).
    public static string Summary(int done, int total) => (done, total) switch
    {
        (1, 1) => "Pronto.",
        (_, 1) => "O aparelho não foi otimizado (veja abaixo).",
        _ when done == total => $"Pronto nos {total} aparelhos.",
        (1, _) => $"Otimizado 1 de {total} aparelhos (veja abaixo).",
        _ => $"Otimizados {done} de {total} aparelhos (veja abaixo).",
    };
}
