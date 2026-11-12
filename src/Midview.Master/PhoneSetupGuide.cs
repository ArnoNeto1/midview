using System.Windows;

namespace Midview.Master;

/// <summary>
/// One "Deu errado?" entry: what the teacher sees, and what to do. <c>Links</c> are the fixed
/// helps this answer points to by name; the window lists them beside it, so none is a dead end.
/// </summary>
internal sealed record GuideHelp(string Problem, string Answer, IReadOnlyList<GuideHelp>? Links = null);

/// <summary>
/// The one-time screen before the first phone: what to have on the table.
/// </summary>
internal sealed record GuideGather(string Title, IReadOnlyList<string> Items);

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
/// The script of the step-by-step guide that prepares a school phone over the USB cable. Texts and
/// prints are for a Xiaomi with MIUI 14 (Redmi 10C). Other MIUI versions get the Settings search
/// tip in each "Deu errado?"; no menu path is written without a print or a source.
/// </summary>
internal static class PhoneSetupGuide
{
    private const string Folder = "Assets/PrepararCelular/";

    // Real prints of the Redmi 10C (nothing personal in them: no network or account names).
    private const string AccountsPrint = Folder + "contas.png";
    private const string AboutPrint = Folder + "sobre.png";
    private const string DeveloperPrint = Folder + "desenvolvedor.png";

    // PROVISIONAL: a drawing, not a print. The Mi account login screen blocks screenshots.
    // Swap it for a real print after the first real preparation (the outline follows the Target
    // fractions, so nothing else changes).
    private const string MiLoginDrawing = Folder + "conta-mi.png";

    /// <summary>Real print of the Settings home, for the window (e.g. the search tip).</summary>
    public const string SettingsImage = Folder + "configuracoes.png";

    /// <summary>"Sobre o telefone" on <see cref="SettingsImage"/>, as fractions of the image.</summary>
    public static Rect SettingsAboutTarget { get; } = new(0.06, 0.23, 0.88, 0.06);

    /// <summary>Real print of "Sobre o telefone", for the window.</summary>
    public const string AboutImage = AboutPrint;

    private static string SearchTip(string word) =>
        $"Dica: nas Configurações, toque na caixa \"Pesquisar nas Configurações\", no topo, e escreva \"{word}\". O celular leva você até o item.";

    private static GuideHelp NotFound(string menu, string word) => new(
        "Não achei o item no meu celular",
        $"Estes passos são do MIUI 14. Em outras versões do MIUI o item \"{menu}\" pode ficar em outro lugar. " + SearchTip(word));

    /// <summary>The screen before the first phone: what to separate.</summary>
    public static GuideGather Gather { get; } = new(
        "Antes de começar, separe:",
        new[]
        {
            "O cabo USB do celular (tem de ser um cabo que passa dados, não só carrega).",
            "Um chip qualquer (pode ser o do seu celular). Ele é usado só por alguns minutos em cada celular e depois você tira.",
            "A agulha da gaveta do chip (ou um clipe de papel aberto).",
            "O e-mail e a senha da conta Google da escola. O Midview não guarda nenhum dos dois.",
        });

    public static GuideHelp UnknownAccount { get; } = new(
        "Celular com conta desconhecida",
        "Sem a senha da conta Mi ou da conta Google, a conta não sai do celular, e com uma conta dentro o Midview não consegue terminar a preparação. " +
        "Separe esse celular e procure quem tem a senha (ou a assistência da Xiaomi, com a nota fiscal). " +
        "Os outros celulares não são afetados: siga com o próximo.");

    public static GuideHelp LockedWithPin { get; } = new(
        "Celular travado com PIN",
        "Desligue o celular. Segure Ligar e Volume para cima ao mesmo tempo até aparecer um menu. " +
        "Escolha \"Wipe Data\", depois \"Wipe All Data\", depois \"Confirm\" e, por fim, \"Reboot to System\". " +
        "O celular liga sozinho em alguns minutos, na tela \"Bem-vindo\". " +
        "Isso só funciona se as contas Mi e Google já foram tiradas; se não foram, o celular trava pedindo a senha delas. " +
        "Depois volte ao passo 2 e siga a ajuda \"Apareceu Bem-vindo\".");

    /// <summary>The phone is ready but does not show up in Midview: a network problem, not a preparation one.</summary>
    public static IReadOnlyList<GuideHelp> NotAppeared { get; } = new GuideHelp[]
    {
        new("Espere um pouco",
            "O celular só aparece no Midview quando entra no Wi-Fi. Espere até 5 minutos depois de o celular mostrar que está pronto."),
        new("Confira o Wi-Fi",
            "O celular tem de estar no mesmo Wi-Fi do computador do professor, com senha comum. Rede com usuário e senha (de empresa) ou com página de login não funciona."),
        new("Chegue o celular perto do roteador",
            "Sinal fraco atrasa a entrada na rede. Deixe o celular na mesma sala do roteador."),
        new("Não prepare o celular de novo",
            "A preparação deu certo: o celular já confia neste computador. O problema é de rede, e não adianta apagar nem preparar outra vez."),
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
            "Qual número está escrito atrás deste celular?",
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

        new("ligar",
            "Ligue o celular",
            new[]
            {
                "Ligue o celular e desbloqueie a tela.",
                "Confira que a bateria tem carga (mais de metade).",
                "Conecte o celular ao Wi-Fi: ele precisa de internet para entrar na conta Mi, mais adiante.",
            },
            null, null,
            "Leva 1 minuto.",
            "O celular está ligado e no Wi-Fi, próximo passo",
            new GuideHelp[]
            {
                new("Apareceu Bem-vindo",
                    "O celular está apagado. Escolha o idioma e o Wi-Fi, e pule a conta Google e a conta Mi até chegar à tela inicial."),
                new("Pede um PIN que ninguém sabe",
                    "Veja a ajuda \"Celular travado com PIN\".", new[] { LockedWithPin }),
                new("O celular não liga",
                    "Ligue o celular no carregador e espere 5 minutos. Se continua sem ligar, separe este celular e use outro."),
            }),

        new("conta-google",
            "Tire a conta Google do celular",
            new[]
            {
                "Abra as Configurações (ícone de engrenagem).",
                "Toque em \"Contas e sincronização\".",
                "Na seção \"GOOGLE\", toque na conta.",
                "Toque em \"Mais\" e depois em \"Remover conta\".",
                "Se o celular pedir, digite a senha de tela do celular ou da conta.",
                "Com uma conta Google no celular, o Midview não consegue terminar a preparação.",
            },
            AccountsPrint, new Rect(0.06, 0.55, 0.88, 0.09),
            "Leva 1 minuto.",
            "Já tirei, próximo passo",
            new GuideHelp[]
            {
                new("Não aparece \"Google\"",
                    "Então não há conta Google neste celular. Siga para o próximo passo."),
                new("Pediu a senha e ninguém sabe",
                    "Veja a ajuda \"Celular com conta desconhecida\": sem a senha, este celular não pode ser preparado.", new[] { UnknownAccount }),
                NotFound("Contas e sincronização", "conta"),
            }),

        new("desenvolvedor",
            "Ligue o modo de desenvolvedor",
            new[]
            {
                "Abra as Configurações.",
                "Toque em \"Sobre o telefone\" (o primeiro item).",
                "Toque 7 vezes seguidas em \"Versão do MIUI\".",
                "Pare quando aparecer \"Você agora é um desenvolvedor\".",
            },
            AboutPrint, new Rect(0.525, 0.57, 0.445, 0.19),
            "Leva menos de 1 minuto.",
            "Apareceu \"Você agora é um desenvolvedor\", próximo passo",
            new GuideHelp[]
            {
                new("Diz que já é desenvolvedor",
                    "Então o modo de desenvolvedor já está ligado neste celular. Siga para o próximo passo."),
                new("Não aparece \"Versão do MIUI\"",
                    "Role a tela de \"Sobre o telefone\" para baixo: o quadrado \"Versão do MIUI\" fica abaixo de \"Versão do Android\"."),
                NotFound("Sobre o telefone", "Sobre o telefone"),
            }),

        new("chip-conta-mi",
            "Coloque o chip e entre na conta Mi",
            new[]
            {
                "Com a agulha da gaveta do chip (ou um clipe), abra a gaveta, na lateral do celular.",
                "Coloque o chip e feche a gaveta.",
                "Abra as Configurações e toque em \"Conta Mi\", no topo da lista.",
                "Toque no botão do Google.",
                "Entre com o e-mail e a senha da conta Google da escola.",
            },
            MiLoginDrawing, new Rect(0.069, 0.606, 0.861, 0.055),
            "Leva uns 2 minutos.",
            "Entrei na conta Mi, próximo passo",
            new GuideHelp[]
            {
                new("Já tem outra conta Mi",
                    "Saia dela primeiro (o celular pede a senha dessa conta) e depois entre com o Google da escola. " +
                    "Se ninguém sabe a senha, veja a ajuda \"Celular com conta desconhecida\".", new[] { UnknownAccount }),
                new("Fica carregando no \"não sou robô\"",
                    "Confira se o celular está no Wi-Fi e se a data e a hora estão no automático. " + SearchTip("data e hora")),
                NotFound("Conta Mi", "conta"),
            }),

        new("chaves",
            "Ligue as três chaves do desenvolvedor",
            new[]
            {
                "Abra as Configurações e toque em \"Configurações adicionais\".",
                "Toque em \"Opções do desenvolvedor\".",
                "Desça a tela e ligue \"Depuração USB\".",
                "Ligue \"Instalar via USB\".",
                "Ligue \"Depuração USB (configurações de segurança)\".",
                "Cada chave mostra um aviso: espere o contador chegar a zero e toque em \"Aceitar\" ou \"Próximo\".",
            },
            DeveloperPrint, new Rect(0.05, 0.10, 0.90, 0.09),
            "Leva uns 3 minutos.",
            "As três chaves estão ligadas, próximo passo",
            new GuideHelp[]
            {
                new("A chave volta a desligar",
                    "O chip não está bem colocado ou a conta Mi não entrou. Volte ao passo 5, confira os dois e tente de novo."),
                new("Pede para inserir um cartão SIM",
                    "Falta o chip. Volte ao passo 5 e coloque o chip na gaveta."),
                new("Não aparece \"Opções do desenvolvedor\"",
                    "Falta ligar o modo de desenvolvedor. Volte ao passo 4. " + SearchTip("desenvolvedor")),
                NotFound("Configurações adicionais", "depuração"),
            }),

        new("sair-contas",
            "Saia das contas e tire o chip",
            new[]
            {
                "Abra as Configurações e toque em \"Conta Mi\", no topo da lista.",
                "Se \"Encontrar dispositivo\" estiver ligado, desligue.",
                "Toque em \"Sair da conta\".",
                "Se o login pelo Google deixou uma conta Google no celular, tire-a como no passo 3: Contas e sincronização, Google, a conta, Mais, Remover conta.",
                "Abra a gaveta do chip e tire o chip, sem desligar o celular.",
                "As três chaves do passo anterior continuam ligadas. Com qualquer conta no celular, o Midview não consegue terminar.",
            },
            null, null,
            "Leva uns 2 minutos.",
            "Já tirei, próximo passo",
            new GuideHelp[]
            {
                new("Pede a senha da conta e ninguém sabe",
                    "Veja a ajuda \"Celular com conta desconhecida\".", new[] { UnknownAccount }),
                new("Não aparece \"Conta Mi\" no topo",
                    "Então não há conta Mi neste celular. Confira só se sobrou conta Google (passo 3) e tire o chip."),
                NotFound("Conta Mi", "conta"),
            }),

        new("cabo",
            "Ligue o celular no computador pelo cabo",
            new[]
            {
                "Ligue o celular neste computador pelo cabo USB.",
                "No celular aparece \"Permitir depuração USB?\": marque \"Sempre permitir deste computador\" e toque em Permitir.",
                "Depois o celular pergunta se pode instalar: toque em Instalar.",
                "O assistente avança sozinho quando vê o celular no cabo.",
            },
            null, null,
            "Leva uns 2 minutos. Depois o Midview termina sozinho, em cerca de 1 minuto.",
            "Liguei o cabo e toquei em Permitir",
            new GuideHelp[]
            {
                new("Não aparece a pergunta",
                    "Troque de porta USB ou de cabo (um cabo só de carregar não serve) e desbloqueie a tela do celular."),
                new("Toquei em Cancelar",
                    "Desligue o cabo e ligue de novo: a pergunta volta a aparecer."),
            }),
    };
}
