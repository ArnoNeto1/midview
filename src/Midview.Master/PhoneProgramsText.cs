using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// One app of the phones' joined list for "Abrir programa" and "Desinstalar
// programa": what the teacher reads beside it, and whether it can be picked
// (an app that came with the phone cannot be uninstalled).
public sealed record PhoneAppPick(string Package, string Name, string Detail, bool CanPick);

// How "Abrir site", "Abrir programa", "Instalar programa" and "Desinstalar
// programa" word things when phones are among the machines, kept apart so it
// can be tested. A site goes to PCs and phones alike (the phone opens it in
// Chrome); a PC's program is a command and a phone's app is one of its own
// list, so each kind has its window, and the PCs' window points to the
// phones' one; an installer is a PC's (.exe, .msi, .bat, .cmd) or a phone's
// (.apk), and goes only to the machines it is for. The phones' own answers
// are shown as they come.
public static class PhoneProgramsText
{
    // What an older phone agent's answer becomes ("Ainda não funciona em celular.").
    public static string OldAgent(MessageType type) => PhoneAgentCommand.OldAgent(type switch
    {
        MessageType.OpenWebsite => "abre sites",
        MessageType.OpenProgram => "abre apps",
        MessageType.InstallProgram => "instala apps",
        MessageType.UninstallPrograms => "desinstala apps",
        _ => "faz isso",
    });

    public static bool OnlyPhones(IReadOnlyCollection<KnownMachine> targets) =>
        targets.Count > 0 && targets.All(machine => machine.IsPhone);

    public static bool HasPhones(IReadOnlyCollection<KnownMachine> targets) => targets.Any(machine => machine.IsPhone);

    // How the machines are counted: "celulares" alone, "aparelhos" with PCs.
    public static string Noun(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? "celulares" : HasPhones(targets) ? "aparelhos" : "máquinas";

    public static string Describe(IReadOnlyList<KnownMachine> targets, bool wholeClass) =>
        TargetsText.Describe(TargetsPreposition.In, targets, wholeClass, Noun(targets));

    // A tile's menu item, named for apps when the menu acts on phones alone;
    // null for an item this does not name.
    public static string? MenuItem(ToolbarAction action, IReadOnlyCollection<KnownMachine> machines)
    {
        var phones = OnlyPhones(machines);
        return action switch
        {
            ToolbarAction.OpenProgram => phones ? "Abrir app" : "Abrir programa",
            ToolbarAction.Install => phones ? "Instalar app..." : "Instalar programa...",
            ToolbarAction.Uninstall => phones ? "Desinstalar app..." : "Desinstalar programa...",
            _ => null,
        };
    }

    // ---- "Abrir site" ----

    public const string PcWebsiteHint =
        "Abre o site no navegador do aluno. Um site bloqueado continua bloqueado, e com a tela bloqueada ele abre por baixo do cadeado.";

    public const string PhoneWebsiteHint =
        "Abre o site no Chrome do celular. Um site bloqueado continua bloqueado, e com a tela bloqueada ele abre quando ela for liberada.";

    public const string PhoneWebsiteNote =
        "Nos celulares, o site abre no Chrome; com a tela bloqueada, ele abre quando ela for liberada.";

    public static string WebsiteHint(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets) ? PhoneWebsiteHint : PcWebsiteHint;

    // Under the hint when phones go along with PCs; null otherwise.
    public static string? WebsiteNote(IReadOnlyCollection<KnownMachine> targets) =>
        !OnlyPhones(targets) && HasPhones(targets) ? PhoneWebsiteNote : null;

    public static string OpenButton(IReadOnlyCollection<KnownMachine> targets) => OnlyPhones(targets)
        ? targets.Count == 1 ? "Abrir no celular" : "Abrir nos celulares"
        : targets.Count == 1 ? "Abrir na máquina" : "Abrir nas máquinas";

    // Over the lines, once every machine answered (or didn't). what: the site(s).
    public static string Opened(IReadOnlyCollection<KnownMachine> targets, int total, int failures, string what) =>
        failures == total ? $"Não abriu: {what}"
        : failures > 0 ? $"Aberto em {total - failures} de {total}: {what}"
        : total == 1 ? $"Aberto: {what}"
        : OnlyPhones(targets) ? $"Aberto nos {total} celulares: {what}"
        : HasPhones(targets) ? $"Aberto nos {total} aparelhos: {what}"
        : $"Aberto nas {total} máquinas: {what}";

    // ---- "Abrir programa" ----

    // In the PCs' window, when phones were among the machines: they choose an app of their own list.
    public static string PhonesNote(int phones) => phones == 1
        ? "O celular abre um app da lista dele, não um comando."
        : $"Os {phones} celulares abrem um app da lista deles, não um comando.";

    public static string PhonesButton(int phones) => phones == 1 ? "Escolher o app do celular..." : "Escolher o app dos celulares...";

    public static string OpenAppHeading(int phones) => phones == 1 ? "Abrir app no celular" : "Abrir app nos celulares";

    public const string OpenAppHint =
        "Abre o app na frente do que o aluno estiver usando. Um app bloqueado continua bloqueado, e com a tela bloqueada ele abre quando ela for liberada.";

    public static string OpenAppButton(int phones) => phones == 1 ? "Abrir no celular" : "Abrir nos celulares";

    public const string PickOneApp = "Escolha um app da lista primeiro.";

    // The menu's saved commands are a PC's: said when phones were left out of one.
    public static string? SavedCommandLeftOut(int phones) => phones switch
    {
        0 => null,
        1 => "O celular ficou de fora: ele abre um app da lista dele (\"Escolher o app do celular...\").",
        _ => $"Os {phones} celulares ficaram de fora: eles abrem um app da lista deles (\"Escolher o app dos celulares...\").",
    };

    // ---- "Desinstalar programa" ----

    public static string UninstallHeading(int phones) => phones == 1 ? "Desinstalar apps do celular" : "Desinstalar apps dos celulares";

    public const string UninstallHint =
        "Desinstala os apps que o Midview instalou (\"Instalar programa\"), sem perguntar nada ao aluno. Os outros, como os que vieram com o celular, não saem por aqui: para esses, use \"Bloquear programas\".";

    public const string UninstallNone = "Marque pelo menos um app primeiro.";

    public static string UninstallButton(int phones) => phones == 1 ? "Desinstalar do celular" : "Desinstalar dos celulares";

    public static string UninstallPhonesNote(int phones) => phones == 1
        ? "O celular tem a lista de apps dele."
        : $"Os {phones} celulares têm a lista de apps deles.";

    public static string UninstallPhonesButton(int phones) => phones == 1 ? "Desinstalar apps do celular..." : "Desinstalar apps dos celulares...";

    // ---- The phones' joined list ----

    // Beside an app the uninstall list cannot take: one that came with the
    // phone, or any other the Midview did not install.
    public const string CameWithThePhone = "veio com o celular: não sai";
    public const string NotByMidview = "não foi instalado pelo Midview";

    // Every app any phone listed, once (PhoneAppBlockText.Union), with what
    // the teacher reads beside it. forUninstall: only an app some phone says
    // the Midview installed, and none says came with it, can be picked (each
    // phone checks again); the others are shown greyed, with why.
    public static IReadOnlyList<PhoneAppPick> Picks(IReadOnlyList<IReadOnlyList<PhoneApp>?> answers, bool forUninstall)
    {
        var answered = answers.Count(answer => answer is not null);
        var blocked = new Dictionary<string, int>(StringComparer.Ordinal);
        var system = new HashSet<string>(StringComparer.Ordinal);
        var byMidview = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in answers.SelectMany(answer => answer ?? Array.Empty<PhoneApp>()))
        {
            if (app.Flags.HasFlag(PhoneAppFlags.Blocked))
            {
                blocked[app.Package] = blocked.GetValueOrDefault(app.Package) + 1;
            }
            if (app.Flags.HasFlag(PhoneAppFlags.System))
            {
                system.Add(app.Package);
            }
            if (app.Flags.HasFlag(PhoneAppFlags.InstalledByMidview))
            {
                byMidview.Add(app.Package);
            }
        }

        return PhoneAppBlockText.Union(answers).Select(row =>
        {
            var package = row.App.Package;
            if (forUninstall && system.Contains(package))
            {
                return new PhoneAppPick(package, row.Name, CameWithThePhone, CanPick: false);
            }
            if (forUninstall && !byMidview.Contains(package))
            {
                return new PhoneAppPick(package, row.Name, NotByMidview, CanPick: false);
            }
            var where = PhoneAppBlockText.RowDetail(row.Phones, answered);
            var blockedOn = forUninstall ? 0 : blocked.GetValueOrDefault(package);
            var block = blockedOn == 0 ? string.Empty
                : blockedOn >= row.Phones ? "bloqueado"
                : $"bloqueado em {blockedOn}";
            return new PhoneAppPick(package, row.Name, string.Join("; ", new[] { where, block }.Where(text => text.Length > 0)), CanPick: true);
        }).ToList();
    }

    // ---- "Instalar programa" ----

    public const string ApkKind =
        "Aplicativo de Android: instala sozinho, sem perguntar nada no celular. Com \"Liberar só estes\" ligado, ele fica bloqueado até você liberá-lo.";

    public const string PcHint =
        "O Midview de cada máquina instala o programa como administrador: não aparece a tela do UAC e ninguém precisa digitar senha. O instalador tem de funcionar sem perguntas (modo silencioso) e instalar para todos os usuários.";

    public const string PhoneHint =
        "O Midview de cada celular instala o app sozinho, sem perguntar nada ao aluno. Escolha o arquivo .apk do app (de onde você confia: a loja do próprio app ou o site de quem o faz).";

    // After the PCs' hint when phones are among the machines.
    public const string MixedInstallNote =
        "Nos celulares, escolha um app de Android (.apk): ele instala sozinho, sem perguntar nada ao aluno.";

    public static string InstallHint(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? PhoneHint : PcHint;

    // What the window offers to pick: a PC's installers, a phone's .apk, or both.
    public static string FileFilter(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? "Aplicativos de Android (*.apk)|*.apk"
        : HasPhones(targets) ? "Instaladores, scripts e aplicativos de Android (*.exe; *.msi; *.bat; *.cmd; *.apk)|*.exe;*.msi;*.bat;*.cmd;*.apk"
        : "Instaladores e scripts (*.exe; *.msi; *.bat; *.cmd)|*.exe;*.msi;*.bat;*.cmd";

    public static string ChooseButton(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? "Escolher o app (.apk)..." : "Escolher instalador...";

    public static string NothingChosen(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? "Nenhum app escolhido (.apk)."
        : HasPhones(targets) ? "Nenhum instalador escolhido (.exe, .msi, .bat ou .cmd; .apk para os celulares)."
        : "Nenhum instalador ou script escolhido (.exe, .msi, .bat ou .cmd).";

    // Whether the file is one this window can send at all, and why not.
    public static string? FileProblem(string name, long size, IReadOnlyCollection<KnownMachine> targets)
    {
        if (ProgramInstall.IsApkName(name))
        {
            if (!HasPhones(targets))
            {
                return $"{name} é um app de Android: os computadores não o instalam.";
            }
            return size > ProgramInstall.MaxApkSize ? $"{name} passa de 200 MB e não pode ser enviado aos celulares." : null;
        }
        if (!ProgramInstall.IsInstallerName(name))
        {
            return OnlyPhones(targets)
                ? $"{name} não é um app de Android (.apk)."
                : $"{name} não é um instalador ou script (.exe, .msi, .bat ou .cmd).";
        }
        if (OnlyPhones(targets))
        {
            return $"{name} é um instalador de computador: os celulares instalam só apps de Android (.apk).";
        }
        return size > ProgramInstall.MaxFileSize ? $"{name} passa de 4 GB e não pode ser enviado." : null;
    }

    // The machines the chosen file goes to: an .apk to the phones, anything else to the PCs.
    public static IReadOnlyList<KnownMachine> InstallTargets(IReadOnlyList<KnownMachine> targets, string fileName) =>
        targets.Where(machine => machine.IsPhone == ProgramInstall.IsApkName(fileName)).ToList();

    // Said under the file when some of the machines are left out of it; null otherwise.
    public static string? InstallLeftOut(IReadOnlyList<KnownMachine> targets, string fileName)
    {
        var left = targets.Count - InstallTargets(targets, fileName).Count;
        if (left == 0)
        {
            return null;
        }
        return ProgramInstall.IsApkName(fileName)
            ? left == 1 ? "Um .apk instala só nos celulares: 1 computador fica de fora." : $"Um .apk instala só nos celulares: {left} computadores ficam de fora."
            : left == 1 ? "Os celulares instalam só apps .apk: 1 celular fica de fora." : $"Os celulares instalam só apps .apk: {left} celulares ficam de fora.";
    }

    // Where the window says it installs once a file is chosen.
    public static string Installing(string fileName, int machines) => ProgramInstall.IsApkName(fileName)
        ? $"Instalando {Path.GetFileName(fileName)} em {machines} celular(es)."
        : $"Instalando {Path.GetFileName(fileName)} em {machines} máquina(s). Pode levar vários minutos.";

    // A phone's answer to the install as the teacher reads it.
    public static string InstallLine(bool succeeded, string detail) =>
        PhoneAgentCommand.IsOldAgentAnswer(succeeded, detail) ? OldAgent(MessageType.InstallProgram) : detail;
}
