using System.Windows;

namespace Midview.Master;

/// <summary>One "Deu errado?" entry: what the teacher sees, and what to do.</summary>
internal sealed record GuideHelp(string Problem, string Answer);

/// <summary>
/// One screen of the "Preparar celulares" guide. <c>Target</c> is the spot to outline in red,
/// as fractions (0 to 1) of the image, so a print can be swapped without redrawing the outline.
/// </summary>
internal sealed record GuideStep(
    string Id,
    string Title,
    IReadOnlyList<string> Taps,
    string? Image,
    Rect? Target,
    string? Timing,
    string NextText,
    IReadOnlyList<GuideHelp> Help);

/// <summary>
/// The script of the step-by-step guide that prepares a school phone. Texts and prints are
/// for a Xiaomi with MIUI 14 (Redmi 10C); the MIUI 12/13 paths live in each "Deu errado?".
/// </summary>
internal static class PhoneSetupGuide
{
    private const string Folder = "Assets/PrepararCelular/";

    // Real prints of the Redmi 10C (Wi-Fi name already blurred).
    private const string AccountsPrint = Folder + "contas.png";
    private const string ResetPrint = Folder + "redefinicao.png";

    // PROVISIONAL: drawings, not prints. The Mi account screen blocks screenshots and the
    // welcome screen only exists after a reset. Swap both for real prints after the first
    // real preparation (the outline follows the Target fractions, so nothing else changes).
    private const string MiAccountDrawing = Folder + "conta-mi.png";
    private const string WelcomeDrawing = Folder + "bem-vindo.png";

    /// <summary>Real print of the Settings home, for the window (e.g. the search tip).</summary>
    public const string SettingsImage = Folder + "configuracoes.png";

    /// <summary>"Sobre o telefone" on <see cref="SettingsImage"/>, as fractions of the image.</summary>
    public static Rect SettingsAboutTarget { get; } = new(0.06, 0.23, 0.88, 0.06);

    /// <summary>Real print of "Sobre o telefone", for the window.</summary>
    public const string AboutImage = Folder + "sobre.png";

    private const string SearchTip =
        "Dica: nas Configurações, toque na caixa \"Pesquisar nas Configurações\", no topo, e escreva a palavra do item. O celular leva você até ele.";

    public static GuideHelp UnknownAccount { get; } = new(
        "Celular com conta desconhecida",
        "Sem a senha da conta Mi ou da conta Google, o celular não pode ser preparado por você: ao restaurar, ele trava pedindo a senha. " +
        "Separe esse celular e procure quem tem a senha da conta (ou a assistência da Xiaomi, com a nota fiscal). " +
        "Os outros celulares não são afetados: siga com o próximo.");

    public static GuideHelp ResetWithoutPin { get; } = new(
        "Restaurar sem o PIN",
        "Desligue o celular. Segure Ligar e Volume para cima ao mesmo tempo até aparecer um menu. " +
        "Escolha \"Wipe Data\", depois \"Wipe All Data\", depois \"Confirm\" e, por fim, \"Reboot to System\". " +
        "O celular liga sozinho em 3 a 7 minutos, na tela \"Bem-vindo\". " +
        "Isso só funciona se as contas Mi e Google já foram tiradas; se não foram, o celular trava pedindo a senha delas.");

    public static IReadOnlyList<GuideHelp> NotAppeared { get; } = new GuideHelp[]
    {
        new("Olhe a mensagem na tela do celular",
            "Se o celular mostra uma mensagem de erro, ela diz o que fazer. Mensagem sobre Wi-Fi ou conexão quer dizer nome ou senha da rede errados (volte e troque o Wi-Fi). " +
            "Mensagem sobre baixar quer dizer rede sem internet ou celular longe do roteador. " +
            "Mensagem sobre conta quer dizer que faltou tirar a conta Google ou Mi (tire e recomece este celular)."),
        new("Confira o Wi-Fi",
            "O Wi-Fi do Midview tem de ser o mesmo do computador do professor, com senha comum. Rede com usuário e senha (de empresa) ou com página de login não funciona."),
        new("Chegue o celular perto do roteador",
            "Sinal fraco atrasa ou impede o download. Deixe o celular na mesma sala do roteador até terminar."),
        new("O celular já mostra a tela do Midview, mas não aparece aqui",
            "A preparação deu certo. O problema é de rede: confira se o celular e este computador estão no mesmo Wi-Fi. Não restaure o celular de novo."),
        new("Nada ajudou",
            "Use \"Recomeçar este celular\": o assistente volta ao passo de restaurar o celular de fábrica."),
    };

    public static IReadOnlyList<GuideHelp> WifiHelp { get; } = new GuideHelp[]
    {
        new("Que Wi-Fi posso usar?",
            "Uma rede com senha comum (WPA2), a mesma do computador do professor. Rede com usuário e senha (de empresa) ou com página de login antes de usar a internet não funciona."),
        new("A senha não é aceita",
            "A senha precisa ter 8 caracteres ou mais. Confira letras maiúsculas e minúsculas: a senha da rede diferencia."),
    };

    public static IReadOnlyList<GuideStep> Steps { get; } = new GuideStep[]
    {
        new("numero",
            "Qual é o número deste celular?",
            Array.Empty<string>(),
            null, null, null,
            "Próximo passo",
            new GuideHelp[]
            {
                new("Não vejo número nenhum atrás do celular",
                    "Escolha um número livre (1, 2, 3...) e escreva-o atrás do celular com adesivo ou caneta. O número vira o nome: o 7 aparece no Midview como \"Celular 07\"."),
                new("Já existe um celular com esse número",
                    "O Midview avisa. Se for o mesmo celular sendo preparado de novo, confirme. Se for outro, escolha outro número."),
            }),

        new("bateria",
            "Ligue o celular na tomada",
            new[]
            {
                "Ligue o celular no carregador e na tomada.",
                "Confira que a tela mostra que está carregando, ou que a bateria passa de metade.",
            },
            null, null,
            "A preparação leva de 10 a 15 minutos, e o celular não pode desligar no meio.",
            "Já está na tomada, próximo passo",
            new GuideHelp[]
            {
                new("O celular não liga ou não carrega",
                    "Troque o cabo ou a tomada e espere 5 minutos. Se continua sem carregar, separe este celular e use outro."),
                new("Não tenho tomada perto",
                    "A bateria precisa estar acima de metade. Com menos que isso, o celular pode desligar no meio e ficar travado."),
            }),

        new("conta-mi",
            "Tire a conta Mi do celular",
            new[]
            {
                "Abra as Configurações (ícone de engrenagem).",
                "Toque em \"Conta Mi\", no topo da lista.",
                "Desligue \"Encontrar dispositivo\".",
                "Toque em \"Sair\" e digite a senha da conta Mi.",
                "Isso vem antes de restaurar: se o celular for restaurado com a conta Mi dentro, ele trava na volta pedindo a senha dessa conta.",
            },
            MiAccountDrawing, new Rect(0.06, 0.235, 0.88, 0.08),
            "Leva 1 minuto.",
            "Já tirei, próximo passo",
            new GuideHelp[]
            {
                new("Não aparece \"Conta Mi\" no topo",
                    "Então não há conta Mi neste celular. Siga para o próximo passo."),
                new("Ninguém sabe a senha da conta",
                    "Veja a ajuda \"Celular com conta desconhecida\": sem a senha, este celular não pode ser preparado."),
                new("Não achei o item no meu celular",
                    "Em celulares com MIUI 12 ou 13, a conta fica em Configurações, \"Conta Mi\" ou \"Mi Account\", e \"Encontrar dispositivo\" fica dentro dela. " + SearchTip),
            }),

        new("conta-google",
            "Tire a conta Google do celular",
            new[]
            {
                "Abra as Configurações.",
                "Toque em \"Contas e sincronização\".",
                "Na seção \"GOOGLE\", toque na conta.",
                "Toque em \"Mais\" e depois em \"Remover conta\".",
                "Se o celular pedir, digite a senha de tela do celular ou da conta.",
                "Isso vem antes de restaurar: com uma conta Google no celular, o Android trava o celular restaurado pedindo essa conta, e o código QR não passa.",
            },
            AccountsPrint, new Rect(0.06, 0.55, 0.88, 0.09),
            "Leva 1 minuto.",
            "Já tirei, próximo passo",
            new GuideHelp[]
            {
                new("Não aparece \"Google\"",
                    "Então não há conta Google neste celular. Siga para o próximo passo."),
                new("Pediu a senha e ninguém sabe",
                    "Veja a ajuda \"Celular com conta desconhecida\": sem a senha, este celular não pode ser preparado."),
                new("Não achei o item no meu celular",
                    "Em celulares com MIUI 12 ou 13, procure \"Contas e sincronização\" em Configurações, ou \"Contas\" em \"Configurações adicionais\". " + SearchTip),
            }),

        new("restaurar",
            "Restaure o celular de fábrica",
            new[]
            {
                "Abra as Configurações.",
                "Toque em \"Sobre o telefone\" (o primeiro item).",
                "Desça até o fim da tela e toque em \"Redefinição de fábrica\".",
                "Toque em \"Limpar todos os dados\" e confirme.",
                "Quando der certo, o celular reinicia sozinho e termina na tela \"Bem-vindo\", pedindo para escolher o idioma.",
            },
            ResetPrint, new Rect(0.48, 0.87, 0.47, 0.06),
            "Leva de 3 a 7 minutos.",
            "Apareceu o Bem-vindo",
            new GuideHelp[]
            {
                new("Não achei \"Redefinição de fábrica\"",
                    "Em celulares com MIUI 12 ou 13, vá em Configurações, \"Configurações adicionais\" e \"Backup e redefinição\". " + SearchTip),
                new("Pediu um PIN ou senha de tela que ninguém sabe",
                    "Veja a ajuda \"Restaurar sem o PIN\" (modo de recuperação do celular)."),
                new("Pediu a senha de uma conta",
                    "Faltou tirar a conta Mi ou Google. Veja a ajuda \"Celular com conta desconhecida\"."),
                new("Passou de 10 minutos e a tela não muda",
                    "Espere mais 5 minutos. Se continua parado, segure Ligar por 15 segundos até desligar e ligue de novo."),
            }),

        new("seis-toques",
            "Toque 6 vezes e escaneie o código",
            new[]
            {
                "Na tela \"Bem-vindo\", toque 6 vezes seguidas numa parte vazia da tela, sempre no mesmo lugar.",
                "Vai abrir a câmera do celular.",
                "Aponte a câmera para o código QR que aparece neste computador.",
                "Toque em Aceitar ou Continuar até aparecer a tela do Midview.",
            },
            WelcomeDrawing, null,
            "Leva uns 10 minutos: o celular entra no Wi-Fi e baixa o Midview sozinho.",
            "Já escaneei o código",
            new GuideHelp[]
            {
                new("A câmera não abriu",
                    "Toque mais rápido, 6 vezes em menos de 3 segundos, sempre no mesmo lugar, numa parte vazia da tela (longe de botões)."),
                new("A câmera abriu mas não lê o código",
                    "Chegue o celular mais perto da tela do computador, sem reflexo, e espere 5 segundos parado."),
                new("Apareceu uma mensagem de erro",
                    "Leia a mensagem. Se fala de rede ou Wi-Fi, o nome ou a senha da rede estão errados, ou a rede está sem internet. " +
                    "Se fala de conta, sobrou a conta Mi ou Google (volte aos passos de tirar as contas). Depois use \"Recomeçar este celular\"."),
                new("O código venceu",
                    "O código vale por 1 hora e funciona uma vez. Volte e peça um código novo."),
            }),
    };
}
