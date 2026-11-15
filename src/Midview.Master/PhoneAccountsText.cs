namespace Midview.Master;

// What the "Contas do aparelho", "Depuração USB" and "Sair das contas do
// Chrome" windows do to the devices.
public enum PhoneAccountsAction
{
    Free,
    Lock,
    ClearChrome,
    FreeDebugging,
    LockDebugging,
}

// Which card of Configurar celular/tablet opened PhoneAccountsWindow.
public enum PhoneAccountsCard
{
    Accounts,
    ClearChrome,
    Debugging,
}

// How those windows word each device's answer, kept apart so it can be
// tested. The agent's own details ("contas do aparelho liberadas por 10
// minutos", "depuração USB travada", "Chrome limpo: ...") are already in
// Portuguese and are shown as they come.
public static class PhoneAccountsText
{
    // An agent from before these commands: it fails a command it doesn't know.
    public const string OldAgent =
        "o Midview deste aparelho é de uma versão anterior: atualize os celulares e tablets (Máquinas da sala > Atualizações) e tente de novo";

    public const string NoIdentity = PhonePinText.NoIdentity;

    // Every Android device runs the same agent, phones and tablets alike.
    public static string Heading(PhoneAccountsAction action, int devices) => action switch
    {
        PhoneAccountsAction.ClearChrome => devices == 1
            ? "Sair das contas do Chrome do celular ou tablet"
            : "Sair das contas do Chrome dos celulares e tablets",
        PhoneAccountsAction.FreeDebugging or PhoneAccountsAction.LockDebugging => devices == 1
            ? "Depuração USB do celular ou tablet"
            : "Depuração USB dos celulares e tablets",
        _ => devices == 1 ? "Contas do celular ou tablet" : "Contas dos celulares e tablets",
    };

    // Said once in the "Contas do aparelho" window.
    public const string AccountsExplanation =
        "As contas ficam travadas nos celulares e tablets da escola: o aluno não põe nem tira contas (uma conta Google dele abriria a Play Store). " +
        "Para pôr a conta Google da escola num aparelho já preparado (com ela, a Play Store mantém o Chrome atualizado), libere por alguns minutos " +
        "e ponha a conta em Configurações > Contas; depois elas travam de novo sozinhas. O Chrome nunca entra nessa conta: o aluno continua " +
        "fazendo login só nos sites (Classroom e outros).";

    // Said once in the "Depuração USB" window.
    public const string DebuggingExplanation =
        "A depuração USB fica travada nos celulares e tablets da escola: o aluno não a liga (com ela, um computador pelo cabo instala apps " +
        "e mexe no aparelho). Para conferir um aparelho pelo cabo, libere por alguns minutos: a depuração liga sozinha e, no fim do tempo, " +
        "desliga e trava de novo.";

    // The warning under it, in the page's word ("celular" or "tablet").
    public static string DebuggingWarning(string word) =>
        $"Enquanto liberada, um computador ligado pelo cabo pode pedir acesso ao {word}; ele pergunta \"Permitir depuração USB?\" na tela.";

    // Said once in the "Sair das contas do Chrome" window, before the button.
    public const string ClearChromeExplanation =
        "Apaga os logins de sites, os cookies, o histórico e as abas do Chrome em cada aparelho, para o próximo aluno abrir o Chrome limpo " +
        "(use no fim da aula). Não dá para desfazer. Os sites bloqueados e as regras do Chrome continuam. Na próxima vez que abrir, o Chrome " +
        "mostra a tela de boas-vindas de novo: o aluno toca em \"Continuar sem fazer login\".";

    // The question before it goes.
    public static string ClearChromeConfirm(int devices) =>
        (devices == 1 ? "Sair das contas do Chrome no aparelho?" : $"Sair das contas do Chrome nos {devices} aparelhos?") +
        " Os logins de sites, os cookies, o histórico e as abas são apagados, e não dá para desfazer.";

    public static string Minutes(int minutes) => PhoneNetworkText.Minutes(minutes);

    // One line per device, from the agent's answer (PhoneAccountsClient:
    // null for an older agent).
    public static string Line((bool Succeeded, string Detail)? answer) => answer is { } said ? said.Detail : OldAgent;

    public static string Working(PhoneAccountsAction action) => action switch
    {
        PhoneAccountsAction.Free or PhoneAccountsAction.FreeDebugging => "liberando…",
        PhoneAccountsAction.Lock or PhoneAccountsAction.LockDebugging => "travando…",
        _ => "limpando o Chrome…",
    };

    // While the devices are being asked.
    public static string Progress(PhoneAccountsAction action, int devices)
    {
        var verb = action switch
        {
            PhoneAccountsAction.Free => "Liberando as contas",
            PhoneAccountsAction.Lock => "Travando as contas",
            PhoneAccountsAction.FreeDebugging => "Liberando a depuração USB",
            PhoneAccountsAction.LockDebugging => "Travando a depuração USB",
            _ => "Saindo das contas do Chrome",
        };
        return devices == 1 ? $"{verb} no aparelho..." : $"{verb} nos {devices} aparelhos...";
    }

    // Above the list, once every device answered (or didn't).
    public static string Summary(PhoneAccountsAction action, int minutes, int done, int total)
    {
        var did = action switch
        {
            PhoneAccountsAction.Free => $"Contas liberadas por {Minutes(minutes)}",
            PhoneAccountsAction.Lock => "Contas travadas",
            PhoneAccountsAction.FreeDebugging => $"Depuração USB liberada e ligada por {Minutes(minutes)}",
            PhoneAccountsAction.LockDebugging => "Depuração USB desligada e travada",
            _ => "Chrome limpo",
        };
        return (done, total) switch
        {
            (1, 1) => $"{did}.",
            (_, 1) => "Não deu certo no aparelho (veja abaixo).",
            _ when done == total => $"{did} nos {total} aparelhos.",
            (0, _) => $"Não deu certo em nenhum dos {total} aparelhos (veja abaixo).",
            _ => $"{did} em {done} dos {total} aparelhos (veja abaixo).",
        };
    }
}
