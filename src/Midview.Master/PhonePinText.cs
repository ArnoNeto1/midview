using Midview.Protocol;

namespace Midview.Master;

// How the "Senha dos celulares e tablets" window words the PIN and each
// device's answer, kept apart so it can be tested. The agent's own details
// ("PIN posto no celular", "o Android recusou o PIN"...) are already in
// Portuguese and are shown as they come, except the few reworded here.
public static class PhonePinText
{
    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não põe o PIN";

    // The agent's words for a phone that already had a lock screen lock when
    // the Midview got there (its reset key waits for one unlock).
    public const string AgentTokenPending = "chave de redefinição ainda não ativa: desbloqueie o celular uma vez";

    public const string TokenPending = "o celular já tinha uma trava de tela: desbloqueie-o uma vez e tente de novo";

    public const string NoIdentity = "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"";

    // What an agent from before the PIN answers to a command it doesn't know.
    private const string NotOnPhone = "Ainda não funciona em celular.";

    // Only digits, at most 4: what the PIN box keeps of whatever was typed or pasted.
    public static string Digits(string? typed) =>
        new((typed ?? string.Empty).Where(c => c is >= '0' and <= '9').Take(PhoneScreenPassword.PinLength).ToArray());

    // "Pôr o PIN" needs exactly 4 digits ("Tirar o PIN" needs none).
    public static bool IsReady(string? pin) => pin is { Length: PhoneScreenPassword.PinLength } && PhoneScreenPassword.IsValid(pin);

    // Beside the PIN box, while it is not complete.
    public static string PinHint(string? pin) => Digits(pin).Length switch
    {
        PhoneScreenPassword.PinLength => string.Empty,
        0 => "Digite 4 números.",
        3 => "Falta 1 número.",
        var typed => $"Faltam {PhoneScreenPassword.PinLength - typed} números.",
    };

    // An agent from before the PIN: it fails a command it doesn't know.
    public static bool IsOldAgent(bool succeeded, string detail) =>
        !succeeded && (detail.Length == 0 || detail == NotOnPhone);

    // One line per device, from the agent's answer (PhonePinClient.Send:
    // null for an older agent).
    public static string Line((bool Succeeded, string Detail)? answer)
    {
        if (answer is not { } said)
        {
            return OldAgent;
        }
        return !said.Succeeded && said.Detail == AgentTokenPending ? TokenPending : said.Detail;
    }

    public static string Working(bool set) => set ? "pondo o PIN…" : "tirando o PIN…";

    // Every Android device runs the same agent, phones and tablets alike.
    public static string Heading(int devices) => devices == 1 ? "Senha do celular ou tablet" : "Senha dos celulares e tablets";

    // While the devices are being asked.
    public static string Progress(bool set, int devices) => (set, devices) switch
    {
        (true, 1) => "Pondo o PIN no aparelho...",
        (true, _) => $"Pondo o PIN nos {devices} aparelhos...",
        (false, 1) => "Tirando o PIN do aparelho...",
        _ => $"Tirando o PIN dos {devices} aparelhos...",
    };

    // Above the list, once every device answered (or didn't).
    public static string Summary(bool set, int done, int total) => (done, total) switch
    {
        (1, 1) => set ? "PIN posto." : "PIN tirado.",
        (_, 1) => set ? "O PIN não foi posto (veja abaixo)." : "O PIN não foi tirado (veja abaixo).",
        _ when done == total => set ? $"PIN posto nos {total} aparelhos." : $"PIN tirado dos {total} aparelhos.",
        (0, _) => set ? $"O PIN não foi posto em nenhum dos {total} aparelhos (veja abaixo)." : $"O PIN não foi tirado de nenhum dos {total} aparelhos (veja abaixo).",
        (1, _) => set ? $"PIN posto em 1 dos {total} aparelhos (veja abaixo)." : $"PIN tirado de 1 dos {total} aparelhos (veja abaixo).",
        _ => set ? $"PIN posto em {done} dos {total} aparelhos (veja abaixo)." : $"PIN tirado de {done} dos {total} aparelhos (veja abaixo).",
    };
}
