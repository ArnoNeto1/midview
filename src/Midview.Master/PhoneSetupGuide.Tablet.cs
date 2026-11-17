using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Midview.Master;

// The guide for a tablet, tried on the school's Multilaser M10 (Android 10,
// no MIUI). The same nine steps with the same ids as a phone's (the progress
// keys on them), in a tablet's words; the Xiaomi-only ones become a plain
// Android tablet's: the chip and the Mi account become "no screen lock", the
// three developer switches become USB debugging alone, and signing out of
// the Mi account becomes checking no account is left. Its prints are the
// M10's own (2026-11-14, freshly reset; the IMEI, IP and MAC covered).
internal static partial class PhoneSetupGuide
{
    private static IReadOnlyList<GuideStep>? _tabletSteps;

    private const string TabletFolder = "Assets/PrepararTablet/";
    private const string TabletSettingsPrint = TabletFolder + "configuracoes.png";
    private const string TabletAboutPrint = TabletFolder + "sobre.png";
    private const string TabletSecurityPrint = TabletFolder + "seguranca.png";
    private const string TabletDeveloperPrint = TabletFolder + "desenvolvedor.png";
    private const string TabletAccountsPrint = TabletFolder + "contas.png";

    /// <summary>The steps for this kind of device, in its words.</summary>
    public static IReadOnlyList<GuideStep> StepsFor(AndroidDeviceKind kind) =>
        kind == AndroidDeviceKind.Tablet ? _tabletSteps ??= Steps.Select(TabletStep).ToList() : Steps;

    /// <summary>The screen before the first device: a tablet needs no chip and no Mi account.</summary>
    public static GuideGather GatherFor(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet
        ? new GuideGather("Antes de começar, separe:", new[]
        {
            "O cabo USB do tablet (tem de ser um cabo que passa dados, não só carrega).",
            "Uma conta Google (pode ser a da escola), para atualizar o Chrome pela Play Store no primeiro tablet, se ele estiver velho. Nos próximos, o Midview instala sozinho o Chrome que guardou. Ela sai do tablet antes do fim.",
        })
        : Gather;

    public static GuideHelp LockedWithPinFor(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? TabletLockedWithPin : LockedWithPin;

    /// <summary>A phone's text in a tablet's words, when the device is a tablet (both words are masculine).</summary>
    public static string ForKind(string text, AndroidDeviceKind kind) =>
        kind != AndroidDeviceKind.Tablet ? text : text
            .Replace("celulares", "tablets", StringComparison.Ordinal)
            .Replace("Celulares", "Tablets", StringComparison.Ordinal)
            .Replace("celular", "tablet", StringComparison.Ordinal)
            .Replace("Celular", "Tablet", StringComparison.Ordinal);

    // Back from a tablet's words, for a window switched from a tablet to a phone.
    private static string ForPhone(string text) => text
        .Replace("tablets", "celulares", StringComparison.Ordinal)
        .Replace("Tablets", "Celulares", StringComparison.Ordinal)
        .Replace("tablet", "celular", StringComparison.Ordinal)
        .Replace("Tablet", "Celular", StringComparison.Ordinal);

    public static GuideHelp ForKind(GuideHelp help, AndroidDeviceKind kind) => kind != AndroidDeviceKind.Tablet ? help
        : new GuideHelp(ForKind(help.Problem, kind), ForKind(help.Answer, kind), help.Links?.Select(link => ForKind(link, kind)).ToList());

    /// <summary>The Tag of an element showing the teacher's own data (a Wi-Fi name, a login, a
    /// password): FollowDeviceWords leaves it, and what is inside it, as it is.</summary>
    public const string AsIs = "AsIs";

    /// <summary>
    /// Keeps every text the window shows in the words of the device it prepares (<paramref name="kind"/>):
    /// the texts there now and the ones set later (the cable's status, the steps), so no "celular"
    /// written for a phone shows to a teacher preparing a tablet, and back. Inputs and elements
    /// tagged <see cref="AsIs"/> are left alone. Called once, when the window is built.
    /// </summary>
    public static void FollowDeviceWords(Window window, Func<AndroidDeviceKind> kind)
    {
        var words = kind();
        string Words(string text) => words == AndroidDeviceKind.Tablet ? ForKind(text, AndroidDeviceKind.Tablet) : ForPhone(text);

        void FixInlines(InlineCollection inlines)
        {
            foreach (var inline in inlines.ToList())
            {
                switch (inline)
                {
                    case { Tag: AsIs }:
                        break;
                    case Run run when Words(run.Text) is var text && text != run.Text:
                        run.Text = text;
                        break;
                    case Span span:
                        FixInlines(span.Inlines);
                        break;
                }
            }
        }

        void Fix(object item)
        {
            switch (item)
            {
                case FrameworkElement { Tag: AsIs }:
                    return;
                case TextBlock block:
                    FixInlines(block.Inlines);
                    break;
                case ContentControl { Content: string content } control and not Window
                    when Words(content) is var text && text != content:
                    control.Content = text;
                    break;
            }
            if (item is DependencyObject parent)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(parent))
                {
                    Fix(child);
                }
            }
        }

        // After a layout pass, so the texts set later (the cable's status, the
        // rows built for a step) are fixed before they show. Layout passes come
        // in bursts for the whole app (the classroom's thumbnails too): one walk
        // per burst, and only while this window shows. A text already in the
        // device's words is left as it is, so this settles at once.
        var pending = false;
        window.LayoutUpdated += (_, _) =>
        {
            if (pending || !window.IsVisible)
            {
                return;
            }
            pending = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                pending = false;
                words = kind();
                Fix(window);
            });
        };
    }

    private static readonly GuideHelp TabletNotFound = new(
        "Não achei o item no meu tablet",
        "Estes passos são do tablet Multilaser M10 (Android 10). Em outros tablets o item pode ter outro nome ou lugar. " +
        "Dica: nas Configurações, toque na lupa, no topo, e escreva o nome do item.");

    private static readonly GuideHelp TabletLockedWithPin = new(
        "Tablet travado com PIN",
        "Desligue o tablet. Segure Ligar e Volume para cima ao mesmo tempo até aparecer um menu (em alguns tablets é Volume para baixo). " +
        "Com os botões de volume, escolha \"Wipe data/factory reset\" e confirme com Ligar; depois escolha \"Reboot system now\". " +
        "O tablet liga sozinho em alguns minutos, na tela \"Bem-vindo\". " +
        "Isso só funciona se a conta Google já foi tirada; se não foi, o tablet trava pedindo a senha dela. " +
        "Depois volte ao passo 2 e siga a ajuda \"Apareceu Bem-vindo\".");

    private static readonly GuideHelp TabletUnknownAccount = new(
        "Tablet com conta desconhecida",
        "Sem a senha da conta Google, a conta não sai do tablet, e com uma conta dentro o Midview não consegue terminar a preparação. " +
        "Separe esse tablet e procure quem tem a senha. Os outros tablets não são afetados: siga com o próximo.");

    private static GuideStep TabletStep(GuideStep phone) => phone.Id switch
    {
        "ligar" => new(phone.Id,
            "Ligue o tablet",
            new[]
            {
                "Ligue o tablet e desbloqueie a tela.",
                "Confira que a bateria tem carga (mais de metade).",
                "Conecte o tablet ao Wi-Fi: ele precisa de internet para atualizar o Chrome, mais adiante.",
            },
            null, null,
            "Leva 1 minuto.",
            "O tablet está ligado e no Wi-Fi, próximo passo",
            new GuideHelp[]
            {
                new("Apareceu Bem-vindo",
                    "O tablet está apagado. Escolha o idioma e o Wi-Fi, pule a conta Google e não ponha senha de tela, até chegar à tela inicial."),
                new("Pede um PIN que ninguém sabe",
                    "Veja a ajuda \"Tablet travado com PIN\".", new[] { TabletLockedWithPin }),
                new("O tablet não liga",
                    "Ligue o tablet no carregador e espere 5 minutos. Se continua sem ligar, separe este tablet e use outro."),
            }),

        "desenvolvedor" => new(phone.Id,
            "Ligue o modo de desenvolvedor",
            new[]
            {
                "Abra as Configurações.",
                "Toque em \"Sobre o tablet\" (o último item da lista).",
                "Toque 7 vezes seguidas em \"Número da versão\" (no fim da tela).",
                "Pare quando aparecer \"Você agora é um desenvolvedor\".",
            },
            TabletAboutPrint, new Rect(0.05, 0.905, 0.9, 0.055),
            "Leva menos de 1 minuto.",
            "Apareceu \"Você agora é um desenvolvedor\", próximo passo",
            new GuideHelp[]
            {
                new("Diz que já é desenvolvedor",
                    "Então o modo de desenvolvedor já está ligado neste tablet. Siga para o próximo passo."),
                TabletNotFound,
            }),

        "chip-conta-mi" => new(phone.Id,
            "Confira que o tablet está sem senha de tela",
            new[]
            {
                "Abra as Configurações e toque em \"Segurança\".",
                "Toque em \"Bloqueio de tela\".",
                "Se estiver em PIN, Padrão ou Senha, escolha \"Nenhum\" ou \"Deslizar\" (o tablet pede a senha atual).",
                "Se o tablet já teve senha de tela e a escola vai usar o PIN do professor nele, restaure-o de fábrica antes: um tablet que já teve senha de tela não aceita esse PIN.",
            },
            TabletSecurityPrint, new Rect(0.03, 0.315, 0.94, 0.05),
            "Leva menos de 1 minuto.",
            "Está sem senha de tela, próximo passo",
            new GuideHelp[]
            {
                new("Pede a senha atual e ninguém sabe",
                    "Veja a ajuda \"Tablet travado com PIN\".", new[] { TabletLockedWithPin }),
                TabletNotFound,
            }),

        "chaves" => new(phone.Id,
            "Ligue a Depuração USB",
            new[]
            {
                "Abra as Configurações e toque em \"Sistema\".",
                "Toque em \"Avançado\" e depois em \"Opções do desenvolvedor\".",
                "Desça a tela e ligue \"Depuração USB\".",
                "No aviso, toque em \"OK\".",
            },
            TabletDeveloperPrint, new Rect(0.05, 0.318, 0.92, 0.052),
            "Leva menos de 1 minuto.",
            "A Depuração USB está ligada, próximo passo",
            new GuideHelp[]
            {
                new("Não aparece \"Opções do desenvolvedor\"",
                    "Falta ligar o modo de desenvolvedor. Volte ao passo 3."),
                TabletNotFound,
            }),

        "conta-google" => new(phone.Id,
            "Tire a conta Google do tablet",
            new[]
            {
                "Com o cabo ligado, o Midview abre sozinho a tela \"Contas\". Se não abrir, abra as Configurações e toque em \"Contas\".",
                "Toque na conta Google.",
                "Toque em \"Remover conta\" e confirme.",
                "Se entrou numa conta Google na Play Store, no passo anterior, é ela que sai agora.",
                "Com uma conta Google no tablet, o Midview não consegue terminar a preparação.",
            },
            TabletSettingsPrint, new Rect(0.03, 0.68, 0.94, 0.05),
            "Leva 1 minuto.",
            "Já tirei, próximo passo",
            new GuideHelp[]
            {
                new("Não aparece conta Google",
                    "Então não há conta Google neste tablet. Siga para o próximo passo."),
                new("Pediu a senha e ninguém sabe",
                    "Veja a ajuda \"Tablet com conta desconhecida\": sem a senha, este tablet não pode ser preparado.", new[] { TabletUnknownAccount }),
                TabletNotFound,
            }),

        "sair-contas" => new(phone.Id,
            "Confira que não sobrou conta",
            new[]
            {
                "Abra as Configurações e toque em \"Contas\".",
                "Se aparecer alguma conta (Google ou outra), toque nela e em \"Remover conta\".",
                "A conta do próprio tablet para os contatos (\"Phone\" ou \"Telefone\") pode ficar: o Midview cuida dela.",
                "Com qualquer outra conta no tablet, o Midview não consegue terminar.",
            },
            TabletAccountsPrint, new Rect(0.03, 0.085, 0.94, 0.07),
            "Leva menos de 1 minuto.",
            "Não sobrou conta, próximo passo",
            new GuideHelp[]
            {
                new("Pede a senha da conta e ninguém sabe",
                    "Veja a ajuda \"Tablet com conta desconhecida\".", new[] { TabletUnknownAccount }),
                TabletNotFound,
            }),

        // The number, Chrome and the cable are the same on a tablet, in its words.
        _ => new(phone.Id,
            ForKind(phone.Title, AndroidDeviceKind.Tablet),
            phone.Taps.Select(tap => ForKind(tap, AndroidDeviceKind.Tablet)).ToList(),
            null, null,
            phone.Timing is { } timing ? ForKind(timing, AndroidDeviceKind.Tablet) : null,
            ForKind(phone.NextText, AndroidDeviceKind.Tablet),
            phone.Help.Select(help => ForKind(help, AndroidDeviceKind.Tablet)).ToList()),
    };
}
