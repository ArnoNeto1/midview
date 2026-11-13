using Midview.Protocol;

namespace Midview.Master;

// What the "Liberar Wi-Fi e modo avião" window does to the devices.
public enum PhoneNetworkAction
{
    Free,
    Lock,
    SetPassword,
    RemovePassword,
}

// How the "Liberar Wi-Fi e modo avião" window words the admin password and
// each device's answer, kept apart so it can be tested. The agent's own
// details ("Wi-Fi e modo avião liberados por 10 minutos"...) are already in
// Portuguese and are shown as they come.
public static class PhoneNetworkText
{
    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não trava o Wi-Fi nem o modo avião";

    public const string NoIdentity = PhonePinText.NoIdentity;

    // Every Android device runs the same agent, phones and tablets alike.
    public static string Heading(int devices) =>
        devices == 1 ? "Wi-Fi e modo avião do celular ou tablet" : "Wi-Fi e modo avião dos celulares e tablets";

    public static string Minutes(int minutes) => minutes == 1 ? "1 minuto" : $"{minutes} minutos";

    // Only ASCII letters and digits, at most 12: what the password box keeps
    // of whatever was typed or pasted.
    public static string Clean(string? typed) =>
        new((typed ?? string.Empty).Where(PhoneAdminPassword.IsAllowed).Take(PhoneAdminPassword.MaxLength).ToArray());

    public static bool IsPasswordReady(string? password) => PhoneAdminPassword.IsValid(password);

    // Beside the password box, while it is not complete.
    public static string PasswordHint(string? typed) => Clean(typed).Length switch
    {
        0 => "De 6 a 12 letras ou números.",
        PhoneAdminPassword.MinLength - 1 => "Falta 1 letra ou número.",
        < PhoneAdminPassword.MinLength and var length => $"Faltam {PhoneAdminPassword.MinLength - length} letras ou números.",
        _ => string.Empty,
    };

    // An agent from before this: it fails a command it doesn't know.
    public static bool IsOldAgent(bool succeeded, string detail) => PhonePinText.IsOldAgent(succeeded, detail);

    // One line per device, from the agent's answer (PhoneNetworkClient:
    // null for an older agent).
    public static string Line((bool Succeeded, string Detail)? answer) => answer is { } said ? said.Detail : OldAgent;

    public static string Working(PhoneNetworkAction action) => action switch
    {
        PhoneNetworkAction.Free => "liberando…",
        PhoneNetworkAction.Lock => "travando…",
        PhoneNetworkAction.SetPassword => "pondo a senha…",
        _ => "tirando a senha…",
    };

    // While the devices are being asked.
    public static string Progress(PhoneNetworkAction action, int devices)
    {
        var (verb, into, outOf) = action switch
        {
            PhoneNetworkAction.Free => ("Liberando o Wi-Fi e o modo avião", "no", "nos"),
            PhoneNetworkAction.Lock => ("Travando o Wi-Fi e o modo avião", "no", "nos"),
            PhoneNetworkAction.SetPassword => ("Pondo a senha de administrador", "no", "nos"),
            _ => ("Tirando a senha de administrador", "do", "dos"),
        };
        return devices == 1 ? $"{verb} {into} aparelho..." : $"{verb} {outOf} {devices} aparelhos...";
    }

    // Above the list, once every device answered (or didn't).
    public static string Summary(PhoneNetworkAction action, int minutes, int done, int total)
    {
        var did = action switch
        {
            PhoneNetworkAction.Free => $"Wi-Fi e modo avião liberados por {Minutes(minutes)}",
            PhoneNetworkAction.Lock => "Wi-Fi e modo avião travados",
            PhoneNetworkAction.SetPassword => "Senha de administrador posta",
            _ => "Senha de administrador tirada",
        };
        var place = action == PhoneNetworkAction.RemovePassword ? "dos" : "nos";
        return (done, total) switch
        {
            (1, 1) => $"{did}.",
            (_, 1) => "Não deu certo no aparelho (veja abaixo).",
            _ when done == total => $"{did} {place} {total} aparelhos.",
            (0, _) => $"Não deu certo em nenhum dos {total} aparelhos (veja abaixo).",
            _ => $"{did} em {done} dos {total} aparelhos (veja abaixo).",
        };
    }
}
