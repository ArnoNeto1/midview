using System.Globalization;
using Midview.Protocol;

namespace Midview.Master;

// What one machine answered when asked which program block it follows;
// Status is null when it didn't answer.
public sealed record MachineBlockState(string Name, AppBlockClient.StatusResult? Status);

// How the block-programs window words things, kept apart so it can be tested.
public static class AppBlockText
{
    // What an Agent from before this feature answers to SetAppBlock /
    // GetAppBlock: a CommandAck with no detail, which CommandResult reads as
    // (false, "").
    public const string OldAgent =
        "o Midview dessa máquina é de uma versão anterior: atualize pela rede ou com o pendrive";

    // What an Agent says when it kept a block newer than the one it was sent
    // (AppBlockCommandHandler).
    private const string KeptNewerPrefix = "mantido o bloqueio mais novo";

    private const int NamesShown = 4;

    // Common programs offered as one-click suggestions. Names are best
    // guesses; the teacher can always type others.
    public static readonly IReadOnlyList<(string Group, string[] Programs)> Suggestions = new[]
    {
        ("Navegadores", new[] { "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe" }),
        ("Jogos e lojas", new[] { "steam.exe", "epicgameslauncher.exe", "robloxplayerbeta.exe", "minecraftlauncher.exe" }),
        ("Mensagens", new[] { "discord.exe", "whatsapp.exe", "telegram.exe" }),
    };

    // The result line for one machine after applying, from the ack.
    public static string ApplyLine(bool succeeded, string detail) =>
        detail.Length == 0 ? OldAgent : detail;

    // The Agent acknowledged, but kept a newer block (another Master's, or one
    // saved with a clock ahead of this PC's): what the teacher asked for is
    // not what the machine does.
    public static bool KeptNewer(bool succeeded, string detail) =>
        succeeded && detail.StartsWith(KeptNewerPrefix, StringComparison.Ordinal);

    public static string Describe(AppBlockPolicy policy) => policy.Mode switch
    {
        AppBlockMode.BlockList => $"bloqueando {policy.Programs.Count} programa(s)",
        AppBlockMode.AllowOnly => policy.Programs.Count == 0
            ? "permitindo só o Windows"
            : $"permitindo só o Windows e {policy.Programs.Count} programa(s)",
        _ => "nenhum programa bloqueado",
    };

    // A newly applied block must beat every block the machines reported, even
    // when this PC's clock is behind another teacher's.
    public static long NextSavedAt(long nowUnixSeconds, IEnumerable<MachineBlockState> reported)
    {
        var highest = reported.Select(state => state.Status?.Policy?.SavedAt ?? 0).DefaultIfEmpty(0).Max();
        return Math.Max(nowUnixSeconds, highest + 1);
    }

    // The block every machine that answered follows, when they all follow the
    // same one: what the window shows as already chosen.
    public static AppBlockPolicy? CommonActivePolicy(IReadOnlyList<MachineBlockState> machines)
    {
        var policies = machines
            .Where(state => state.Status is { OldAgent: false, Policy: not null })
            .Select(state => state.Status!.Policy!)
            .ToList();
        if (policies.Count == 0 || !policies[0].IsActive || policies.Any(policy => !policy.SameRulesAs(policies[0]) || policy.Mode != policies[0].Mode))
        {
            return null;
        }
        return policies.MaxBy(policy => policy.SavedAt);
    }

    // The "Estado atual" text: machines grouped by what they follow.
    public static string Summarize(IReadOnlyList<MachineBlockState> machines)
    {
        var lines = new List<string>();

        var answered = machines.Where(state => state.Status is { OldAgent: false, Policy: not null }).ToList();
        var groups = answered
            .GroupBy(state => (state.Status!.Policy!.Mode, Key: string.Join('|', state.Status.Policy.Programs.OrderBy(p => p, StringComparer.Ordinal))))
            .OrderByDescending(group => group.Key.Mode != AppBlockMode.None)
            .ThenByDescending(group => group.Count());
        foreach (var group in groups)
        {
            var policy = group.Select(state => state.Status!.Policy!).MaxBy(candidate => candidate.SavedAt)!;
            var where = $"em {MachinesText(group.Count())}";
            lines.Add(policy.Mode switch
            {
                AppBlockMode.BlockList => $"Bloqueando {Names(policy.Programs)} {where} (salvo em {SavedAtText(policy.SavedAt)})",
                AppBlockMode.AllowOnly => policy.Programs.Count == 0
                    ? $"Permitindo só o Windows {where} (salvo em {SavedAtText(policy.SavedAt)})"
                    : $"Permitindo só o Windows e {Names(policy.Programs)} {where} (salvo em {SavedAtText(policy.SavedAt)})",
                _ => $"Nenhum programa bloqueado {where}",
            });
        }

        var old = machines.Where(state => state.Status is { OldAgent: true }).Select(state => state.Name).ToList();
        if (old.Count > 0)
        {
            lines.Add($"Midview de versão anterior (atualize para bloquear): {Names(old)}");
        }

        var silent = machines.Where(state => state.Status is null).Select(state => state.Name).ToList();
        if (silent.Count > 0)
        {
            lines.Add($"Sem resposta: {Names(silent)}");
        }

        return lines.Count == 0 ? "Nenhuma máquina respondeu." : string.Join("\n", lines);
    }

    // The small badge on a machine's tile; empty when there is nothing to say.
    // classBlockExpected: the Master holds a class-wide block it wants this
    // machine to follow, which an older Midview can't.
    public static string Badge(AppBlockClient.StatusResult? status, bool classBlockExpected)
    {
        if (status is null)
        {
            return string.Empty;
        }
        if (status.OldAgent)
        {
            return classBlockExpected ? "⚠ Midview antigo: sem bloqueio de programas" : string.Empty;
        }

        return status.Policy switch
        {
            { Mode: AppBlockMode.BlockList } policy => $"🔒 {policy.Programs.Count} {(policy.Programs.Count == 1 ? "bloqueado" : "bloqueados")}",
            { Mode: AppBlockMode.AllowOnly, Programs.Count: 0 } => "🔒 só o Windows",
            { Mode: AppBlockMode.AllowOnly } policy => $"🔒 só {policy.Programs.Count} {(policy.Programs.Count == 1 ? "permitido" : "permitidos")}",
            _ => string.Empty,
        };
    }

    private static string MachinesText(int count) => count == 1 ? "1 máquina" : $"{count} máquinas";

    private static string Names(IReadOnlyCollection<string> names)
    {
        var shown = names.Take(NamesShown);
        var rest = names.Count - NamesShown;
        return rest > 0 ? $"{string.Join(", ", shown)} e mais {rest}" : string.Join(", ", shown);
    }

    private static string SavedAtText(long savedAt) =>
        DateTimeOffset.FromUnixTimeSeconds(savedAt).ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
}
