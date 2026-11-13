using Midview.Protocol;

namespace Midview.Master;

// What to do with one phone, from its update status and the APK to send.
public enum PhoneUpdateStep
{
    UpToDate,
    Push,
    OldAgent,
}

// How "Atualizar celulares" decides and words each phone, kept apart so it
// can be tested. The phone's own reasons ("assinado com outra chave"...) are
// shown as they come. The same window updates the tablets ("Atualizar os
// tablets"): the kind only changes the words, both being masculine.
public static class PhoneUpdateText
{
    // An agent from before updating answers the offer and the status
    // question with this CommandAck (CommandResult.NOT_ON_PHONE on the phone).
    private const string NotOnPhone = "Ainda não funciona em celular.";

    public const string OldAgent =
        "o Midview deste celular é de uma versão anterior e não se atualiza pela rede: instale-o de novo pelo cabo";

    // While the APK goes, is checked and installed, and the agent restarts in
    // the new version (it does not answer for a few seconds then).
    public const string Updating = "atualizando…";

    public const string Checking = "verificando a versão…";

    private static string One(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "tablet" : "celular";

    private static string Many(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "tablets" : "celulares";

    // The words of a window for these devices: a tablet's only when they are
    // all tablets.
    public static AndroidDeviceKind KindOf(IReadOnlyCollection<KnownMachine> devices) =>
        devices.Count > 0 && devices.All(device => DeviceSupport.IsAndroidOf(device, AndroidDeviceKind.Tablet))
            ? AndroidDeviceKind.Tablet
            : AndroidDeviceKind.Phone;

    public static string OldAgentOf(AndroidDeviceKind kind) =>
        $"o Midview deste {One(kind)} é de uma versão anterior e não se atualiza pela rede: instale-o de novo pelo cabo";

    public static string Intro(AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"Manda pela rede o Midview desta versão para os {Many(kind)}: um {One(kind)} primeiro e, quando ele confirmar que a versão nova funciona, os outros. "
        + $"O aluno não precisa tocar em nada. O Midview do {One(kind)} some por alguns segundos e volta sozinho, e os bloqueios continuam valendo. "
        + "O Android mostra ao aluno o aviso \"Atualizado pelo seu administrador\".";

    public static string NoIdentity(AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"falta a identidade do {One(kind)}: pareie-o de novo em \"Máquinas da sala\"";

    public static string CheckingApk(AndroidDeviceKind kind = AndroidDeviceKind.Phone) => $"Conferindo o app do {One(kind)}...";

    public static string ApkFailed(string reason, AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"O app do {One(kind)} não pôde ser obtido: {reason}.";

    public static string UpdatingFirst(AndroidDeviceKind kind = AndroidDeviceKind.Phone) => $"Atualizando o primeiro {One(kind)}...";

    public static string UpdatingOthers(AndroidDeviceKind kind = AndroidDeviceKind.Phone) => $"Atualizando os outros {Many(kind)}...";

    // The rows left waiting, and the window's line, when the first one failed.
    public static string NotStarted(AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"não começou: o primeiro {One(kind)} não confirmou a versão nova";

    public static string FirstFailed(AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"O primeiro {One(kind)} não confirmou a versão nova; os outros não foram atualizados.";

    // The status bar, when the room or the menu's machines have none.
    public static string NoneToUpdate(AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"Nenhum {One(kind)} na lista da sala para atualizar.";

    // The card in "Máquinas da sala" > "Atualizações" with none of them in the room.
    public static string NoneInRoom(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet
        ? "Nenhum tablet na sala ainda: prepare um em \"Configurar tablet\"."
        : "Nenhum celular na sala ainda.";

    // status: null for an agent from before updating (StatusResult.OldAgent).
    // apkSha256Hex: GitHub's digest is lower case, the phone's upper case.
    public static PhoneUpdateStep Plan(UpdateStatusReport? status, string apkSha256Hex)
    {
        if (status is null)
        {
            return PhoneUpdateStep.OldAgent;
        }
        return status.Ready && string.Equals(status.Fingerprint, apkSha256Hex, StringComparison.OrdinalIgnoreCase)
            ? PhoneUpdateStep.UpToDate
            : PhoneUpdateStep.Push;
    }

    // The offer turned down by an agent that doesn't know it.
    public static bool IsOldAgent(PushResult push) =>
        push.Outcome == PushOutcome.OldAgent || (push.Outcome == PushOutcome.Refused && push.Detail == NotOnPhone);

    // The phone's version when the window opens, before anything is sent.
    public static string Installed(StatusResult status, AndroidDeviceKind kind = AndroidDeviceKind.Phone) => status switch
    {
        { OldAgent: true } or { Report: null } => OldAgentOf(kind),
        { Report.Ready: false } => "calculando a versão instalada…",
        { Report: var report } => $"versão {report.Build ?? "desconhecida"}",
    };

    public static string UpToDate(string? build) => $"já está na versão {build ?? "nova"}";

    public static string Updated(string? build) => $"atualizado para {build ?? "a versão nova"}";

    public static string NotUpdated(string reason) => $"não atualizou: {reason}";

    public static string Sending(long percent) => $"enviando o app… {percent}%";

    // The same APK for both kinds: the tablet runs the phone app.
    public static string ApkLine(string version, bool kept, AndroidDeviceKind kind = AndroidDeviceKind.Phone) => kept
        ? $"App do {One(kind)}, versão {version}: já está guardado neste PC."
        : $"App do {One(kind)}, versão {version}: ainda não está neste PC. Ao atualizar, ele é baixado do GitHub.";

    public static string Downloading(int percent, AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        $"Baixando o app do {One(kind)} do GitHub… {percent}%";

    // A tile's menu item, for the phones among the machines it acts on.
    public static string MenuItem(int phones, AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        phones == 1 ? $"Atualizar o Midview do {One(kind)}..." : $"Atualizar o Midview dos {Many(kind)}...";

    public static string Heading(int phones, AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        phones == 1 ? $"Atualizar o {One(kind)}" : $"Atualizar os {Many(kind)}";

    // current: phones now on the APK's version (updated or already there).
    public static string Summary(int current, int total, AndroidDeviceKind kind = AndroidDeviceKind.Phone) => (current, total) switch
    {
        (1, 1) => $"Terminado: o {One(kind)} está na versão nova.",
        (_, 1) => $"Terminado: o {One(kind)} não ficou na versão nova (veja abaixo).",
        _ when current == total => $"Terminado: os {total} {Many(kind)} estão na versão nova.",
        _ => $"Terminado: {current} de {total} {Many(kind)} na versão nova (veja abaixo).",
    };
}
