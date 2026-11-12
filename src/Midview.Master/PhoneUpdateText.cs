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
// shown as they come.
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
    public static string Installed(StatusResult status) => status switch
    {
        { OldAgent: true } or { Report: null } => OldAgent,
        { Report.Ready: false } => "calculando a versão instalada…",
        { Report: var report } => $"versão {report.Build ?? "desconhecida"}",
    };

    public static string UpToDate(string? build) => $"já está na versão {build ?? "nova"}";

    public static string Updated(string? build) => $"atualizado para {build ?? "a versão nova"}";

    public static string NotUpdated(string reason) => $"não atualizou: {reason}";

    public static string Sending(long percent) => $"enviando o app… {percent}%";

    public static string ApkLine(string version, bool kept) => kept
        ? $"App do celular, versão {version}: já está guardado neste PC."
        : $"App do celular, versão {version}: ainda não está neste PC. Ao atualizar, ele é baixado do GitHub.";

    public static string Downloading(int percent) => $"Baixando o app do celular do GitHub… {percent}%";

    // A tile's menu item, for the phones among the machines it acts on.
    public static string MenuItem(int phones) =>
        phones == 1 ? "Atualizar o Midview do celular..." : "Atualizar o Midview dos celulares...";

    public static string Heading(int phones) => phones == 1 ? "Atualizar o celular" : "Atualizar os celulares";

    // current: phones now on the APK's version (updated or already there).
    public static string Summary(int current, int total) => (current, total) switch
    {
        (1, 1) => "Terminado: o celular está na versão nova.",
        (_, 1) => "Terminado: o celular não ficou na versão nova (veja abaixo).",
        _ when current == total => $"Terminado: os {total} celulares estão na versão nova.",
        _ => $"Terminado: {current} de {total} celulares na versão nova (veja abaixo).",
    };
}
