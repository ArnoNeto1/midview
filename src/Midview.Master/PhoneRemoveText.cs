using Midview.Protocol;

namespace Midview.Master;

// The two ways "Tirar o Midview dos celulares e tablets" takes the Midview
// off a device: undoing what the school did and uninstalling it (the
// student's files kept), or erasing it back to factory settings.
public enum PhoneRemoveChoice
{
    Undo,
    FactoryReset,
}

// How the "Tirar o Midview dos celulares e tablets" window words the two
// choices, the word typed to confirm and each device's answer, kept apart so
// it can be tested. The agent's own details ("Midview saindo: ...") are
// already in Portuguese and are shown as they come.
public static class PhoneRemoveText
{
    public const string OldAgent =
        "o Midview deste aparelho é de uma versão anterior e ainda não sai sozinho: atualize-o primeiro (ou use Restaurar de fábrica)";

    public const string OldAgentWipe = "o Midview deste aparelho é de uma versão anterior e ainda não aceita o pedido de apagar";

    public const string NoIdentity = PhonePinText.NoIdentity;

    public const string WipeStarted = "apagando: o aparelho volta de fábrica";

    // Said once, under the choices.
    public const string OnThePhone =
        "No próprio aparelho também dá: na notificação do Midview, toque em \"Ajustes da escola\" e em \"Tirar o Midview deste celular\" (pede a senha de administrador dos celulares).";

    public const string LeavesTheRoom = "Os aparelhos em que der certo saem da lista da sala.";

    public static string Heading(int devices) =>
        devices == 1 ? "Tirar o Midview do celular ou tablet" : "Tirar o Midview dos celulares e tablets";

    public static string Title(PhoneRemoveChoice choice) => choice switch
    {
        PhoneRemoveChoice.FactoryReset => "Restaurar de fábrica",
        _ => "Desfazer e tirar o Midview",
    };

    public static string Explanation(PhoneRemoveChoice choice) => choice switch
    {
        PhoneRemoveChoice.FactoryReset =>
            "Apaga tudo: fotos, arquivos, contas e apps. O aparelho volta como saiu da loja e pede para ser configurado de novo. Não dá para desfazer.",
        _ =>
            "Mantém as fotos e os arquivos dos alunos. O aparelho volta a ser comum: saem as travas e os ajustes da escola " +
            "(Wi-Fi, apps e sites bloqueados, PIN da tela, papel de parede, DNS sem anúncios), voltam os apps do sistema " +
            "tirados na preparação e o Midview se desinstala (o aparelho pede um toque para confirmar). Não voltam a conta " +
            "Mi tirada na preparação nem os apps baixados que o \"Otimizar\" desinstalou.",
    };

    // The word the teacher types to confirm the choice.
    public static string Word(PhoneRemoveChoice choice) =>
        choice == PhoneRemoveChoice.FactoryReset ? PhoneLeave.WipeWord : PhoneLeave.Word;

    public static string Prompt(PhoneRemoveChoice choice) => $"Para confirmar, digite {Word(choice)}:";

    // The word as the choice wants it, in any case, spaces around it ignored.
    public static bool IsConfirmed(PhoneRemoveChoice choice, string? typed) =>
        string.Equals((typed ?? string.Empty).Trim(), Word(choice), StringComparison.OrdinalIgnoreCase);

    public static string Button(PhoneRemoveChoice choice) =>
        choice == PhoneRemoveChoice.FactoryReset ? "Apagar os aparelhos" : "Tirar o Midview";

    public static string Working(PhoneRemoveChoice choice) =>
        choice == PhoneRemoveChoice.FactoryReset ? "pedindo para apagar…" : "tirando as travas (pode levar um minuto)…";

    public static string Progress(PhoneRemoveChoice choice, int devices) => (choice, devices) switch
    {
        (PhoneRemoveChoice.FactoryReset, 1) => "Pedindo ao aparelho para voltar de fábrica...",
        (PhoneRemoveChoice.FactoryReset, _) => $"Pedindo aos {devices} aparelhos para voltar de fábrica...",
        (_, 1) => "Tirando o Midview do aparelho...",
        _ => $"Tirando o Midview dos {devices} aparelhos...",
    };

    // An agent from before the command: it fails a command it doesn't know.
    public static bool IsOldAgent(bool succeeded, string detail) => PhonePinText.IsOldAgent(succeeded, detail);

    // One line per device, from the agent's answer (PhoneRemoveClient: null
    // for an older agent).
    public static string Line(PhoneRemoveChoice choice, (bool Succeeded, string Detail)? answer) => answer switch
    {
        null => choice == PhoneRemoveChoice.FactoryReset ? OldAgentWipe : OldAgent,
        { Succeeded: true } when choice == PhoneRemoveChoice.FactoryReset => WipeStarted,
        { } said => said.Detail,
    };

    // After "Desfazer e tirar o Midview", for the devices where it did not
    // go: a phone that kept a lock is still the school's and can be erased.
    public static string OfferReset(int failed) => failed == 1
        ? "No aparelho em que não deu certo, o Midview continua: tente de novo ou escolha Restaurar de fábrica (vale só para ele)."
        : $"Nos {failed} aparelhos em que não deu certo, o Midview continua: tente de novo ou escolha Restaurar de fábrica (vale só para eles).";

    // Above the list, once every device answered (or didn't).
    public static string Summary(PhoneRemoveChoice choice, int done, int total)
    {
        var did = choice == PhoneRemoveChoice.FactoryReset ? "Pedido para voltar de fábrica" : "Midview tirado";
        return (done, total) switch
        {
            (1, 1) => $"{did}. O aparelho saiu da lista da sala.",
            (_, 1) => "Não deu certo no aparelho (veja abaixo).",
            _ when done == total => $"{did} nos {total} aparelhos. Eles saíram da lista da sala.",
            (0, _) => $"Não deu certo em nenhum dos {total} aparelhos (veja abaixo).",
            _ => $"{did} em {done} dos {total} aparelhos; esses saíram da lista da sala (veja abaixo).",
        };
    }
}
