using System.Globalization;
using Midview.Protocol;

namespace Midview.Master;

// What one phone answered when asked which app block it follows; Status is
// null when it didn't answer.
public sealed record PhoneBlockState(string Name, PhoneAppBlockClient.StatusResult? Status);

// One app of the phones' joined list: as the first phone that has it
// describes it, and on how many phones it is.
public sealed record PhoneAppRow(PhoneApp App, int Phones)
{
    public string Name => App.Label.Length > 0 ? App.Label : App.Package;
}

// One app of the window's list, ticked or not (its icon is the window's).
public sealed record PhoneAppChoice(string Package, string Name, string Detail, bool Checked);

// How the phones' block-apps window and the phone tiles word things, kept
// apart so it can be tested. The phone's own answers ("bloqueando N app(s)",
// "mantido o bloqueio mais novo...") are shown as they come.
public static class PhoneAppBlockText
{
    // A phone agent from before app blocking answers an unknown command with
    // this (CommandResult.NOT_ON_PHONE on the phone).
    private const string NotOnPhone = "Ainda não funciona em celular.";

    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não bloqueia apps";

    private const int NamesShown = 4;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string ApplyLine(bool succeeded, string detail) =>
        detail.Length == 0 || (!succeeded && detail == NotOnPhone) ? OldAgent : detail;

    // The phone acknowledged, but kept a newer block: the same words as a PC's Agent.
    public static bool KeptNewer(bool succeeded, string detail) => AppBlockText.KeptNewer(succeeded, detail);

    // Newer than every block the phones reported and than the class block
    // this PC keeps, even when this PC's clock is behind.
    public static long NextSavedAt(long nowUnixSeconds, IEnumerable<PhoneBlockState> reported, PhoneAppPolicy? classPolicy)
    {
        var highest = reported.Select(state => state.Status?.Policy?.SavedAt ?? 0)
            .Append(classPolicy?.SavedAt ?? 0)
            .Max();
        return Math.Max(nowUnixSeconds, highest + 1);
    }

    // The block every phone that answered follows, when they all follow the
    // same one: what the window shows as already chosen.
    public static PhoneAppPolicy? CommonActivePolicy(IReadOnlyList<PhoneBlockState> phones)
    {
        var policies = phones
            .Where(state => state.Status is { OldAgent: false, Policy: not null })
            .Select(state => state.Status!.Policy!)
            .ToList();
        if (policies.Count == 0 || !policies[0].IsActive || policies.Any(policy => !policy.SameRulesAs(policies[0])))
        {
            return null;
        }
        return policies.MaxBy(policy => policy.SavedAt);
    }

    // The "estado atual" text: phones grouped by what they follow. labelOf
    // names a package as the phones' list does (the package itself when unknown).
    public static string Summarize(IReadOnlyList<PhoneBlockState> phones, Func<string, string>? labelOf = null)
    {
        labelOf ??= package => package;
        var lines = new List<string>();

        var groups = phones
            .Where(state => state.Status is { OldAgent: false, Policy: not null })
            .GroupBy(state => (state.Status!.Policy!.Mode, Key: string.Join('|', state.Status.Policy.Packages.OrderBy(p => p, StringComparer.Ordinal))))
            .OrderByDescending(group => group.Key.Mode != AppBlockMode.None)
            .ThenByDescending(group => group.Count());
        foreach (var group in groups)
        {
            var policy = group.Select(state => state.Status!.Policy!).MaxBy(candidate => candidate.SavedAt)!;
            var where = $"em {PhonesText(group.Count())}";
            var names = Names(policy.Packages.Select(labelOf).ToList());
            var saved = $"(salvo em {SavedAtText(policy.SavedAt)})";
            lines.Add(policy.Mode switch
            {
                AppBlockMode.BlockList => $"Bloqueando {names} {where} {saved}",
                AppBlockMode.AllowOnly => policy.Packages.Count == 0
                    ? $"Permitindo só o essencial {where} {saved}"
                    : $"Permitindo só o essencial e {names} {where} {saved}",
                _ => $"Nenhum app bloqueado {where}",
            });
        }

        var old = phones.Where(state => state.Status is { OldAgent: true }).Select(state => state.Name).ToList();
        if (old.Count > 0)
        {
            lines.Add($"Midview de versão anterior (atualize para bloquear apps): {Names(old)}");
        }

        var silent = phones.Where(state => state.Status is null).Select(state => state.Name).ToList();
        if (silent.Count > 0)
        {
            lines.Add($"Sem resposta: {Names(silent)}");
        }

        return lines.Count == 0 ? "Nenhum celular respondeu." : string.Join("\n", lines);
    }

    // The small badge on a phone's tile; empty when there is nothing to say.
    // classBlockExpected: the Master holds a class-wide phone block it wants
    // this phone to follow, which an older Midview can't.
    public static string Badge(PhoneAppBlockClient.StatusResult? status, bool classBlockExpected)
    {
        if (status is null)
        {
            return string.Empty;
        }
        if (status.OldAgent)
        {
            return classBlockExpected ? "⚠ Midview antigo: sem bloqueio de apps" : string.Empty;
        }

        return status.Policy switch
        {
            { Mode: AppBlockMode.BlockList } policy => policy.Packages.Count == 1
                ? "🔒 1 app bloqueado"
                : $"🔒 {policy.Packages.Count} apps bloqueados",
            { Mode: AppBlockMode.AllowOnly, Packages.Count: 0 } => "🔒 só o essencial",
            { Mode: AppBlockMode.AllowOnly } policy => policy.Packages.Count == 1
                ? "🔒 só 1 app liberado"
                : $"🔒 só {policy.Packages.Count} apps liberados",
            _ => string.Empty,
        };
    }

    // Every app any phone listed, once, sorted by name; label and icon from
    // the first phone that has it. A phone that didn't answer is null.
    public static IReadOnlyList<PhoneAppRow> Union(IEnumerable<IReadOnlyList<PhoneApp>?> answers)
    {
        var first = new Dictionary<string, PhoneApp>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var apps in answers)
        {
            foreach (var app in apps ?? Array.Empty<PhoneApp>())
            {
                first.TryAdd(app.Package, app);
                counts[app.Package] = counts.GetValueOrDefault(app.Package) + 1;
            }
        }

        return first.Values
            .Select(app => new PhoneAppRow(app, counts[app.Package]))
            .OrderBy(row => row.Name, StringComparer.Create(PtBr, ignoreCase: true))
            .ThenBy(row => row.App.Package, StringComparer.Ordinal)
            .ToList();
    }

    // Beside an app in the list: on how many of the phones that answered it
    // is, said only when not on all of them.
    public static string RowDetail(int phones, int answered) => phones switch
    {
        0 => answered switch
        {
            0 => string.Empty,
            1 => "não está na lista do celular",
            _ => "não está na lista dos celulares",
        },
        _ when phones >= answered => string.Empty,
        _ => $"em {phones} de {answered} celulares",
    };

    // Above the list: whose apps it shows.
    public static string ListLine(int answered, int asked) =>
        answered == 0
            ? asked == 1
                ? "O celular não mandou a lista de apps. Confira se está ligado e use \"Atualizar lista\"."
                : "Nenhum celular mandou a lista de apps. Confira se estão ligados e use \"Atualizar lista\"."
        : asked == 1 ? "Apps que o aluno pode abrir neste celular."
        : answered == asked ? $"Apps de todos os {asked} celulares."
        : asked - answered == 1 ? $"Apps de {answered} de {asked} celulares (o outro não respondeu)."
        : $"Apps de {answered} de {asked} celulares (os outros não responderam).";

    // The window's heading and the title over its list.
    public static string Heading(int phones) => phones == 1 ? "Bloquear apps no celular" : "Bloquear apps nos celulares";

    public static string ListTitle(int phones) => phones == 1 ? "Apps do celular" : "Apps dos celulares";

    // Said after applying when a phone kept a newer block than the one sent.
    public static string KeptNewerLine(int phones) =>
        (phones == 1
            ? "O celular já tinha um bloqueio mais novo (de outro professor, ou salvo com o relógio adiantado) e o seu não valeu nele. "
            : "Algum celular já tinha um bloqueio mais novo (de outro professor, ou salvo com o relógio adiantado) e o seu não valeu nele. ")
        + "Veja o estado atual acima e aplique de novo se quiser trocar.";

    // A tile's menu item: the phones' window opens only for phones alone.
    public static string BlockProgramsMenuItem(IReadOnlyCollection<KnownMachine> targets) =>
        targets.Count > 0 && targets.All(machine => machine.IsPhone) ? "Bloquear apps..." : "Bloquear programas...";

    // The apps of the block the phones follow, ticked, shown as soon as their
    // state is read: the mode it preselects is never shown with nothing ticked
    // while the lists are still on their way.
    public static IReadOnlyList<PhoneAppChoice> InForce(PhoneAppPolicy policy, Func<string, string> nameOf) =>
        policy.Packages
            .Select(package => new PhoneAppChoice(
                package, nameOf(package), policy.Mode == AppBlockMode.AllowOnly ? "liberado agora" : "bloqueado agora", true))
            .ToList();

    // The list once phones sent theirs (answered of them): every app they
    // listed, ticked when it was, then each ticked app none of them listed.
    public static IReadOnlyList<PhoneAppChoice> Choices(
        IReadOnlyList<PhoneAppRow> union, int answered, IReadOnlyCollection<string> ticked, Func<string, string> nameOf)
    {
        var left = new HashSet<string>(ticked, StringComparer.Ordinal);
        var choices = union
            .Select(row => new PhoneAppChoice(row.App.Package, row.Name, RowDetail(row.Phones, answered), left.Remove(row.App.Package)))
            .ToList();
        choices.AddRange(ticked
            .Where(left.Contains)
            .Distinct(StringComparer.Ordinal)
            .Select(package => new PhoneAppChoice(package, nameOf(package), RowDetail(0, answered), true)));
        return choices;
    }

    // The line under the window's title. allPhones: every phone (every
    // tablet, for kind Tablet) in the classroom list.
    public static string Targets(IReadOnlyList<string> names, bool allPhones, AndroidDeviceKind kind = AndroidDeviceKind.Phone)
    {
        var many = kind == AndroidDeviceKind.Tablet ? "tablets" : "celulares";
        if (names.Count == 1)
        {
            return $"Em: {names[0]}";
        }
        if (allPhones)
        {
            return $"Em todos os {many} da sala ({names.Count})";
        }
        return $"Em {names.Count} {many}: {Names(names)}";
    }

    // "Bloquear programas" on PCs and phones together opens the PCs' window;
    // null when no phone was among them.
    public static string? LeftOutNotice(int phones) => phones switch
    {
        <= 0 => null,
        1 => "1 celular ficou de fora: para bloquear apps no celular, selecione só o celular.",
        _ => $"{phones} celulares ficaram de fora: para bloquear apps nos celulares, selecione só os celulares.",
    };

    private static string PhonesText(int count) => count == 1 ? "1 celular" : $"{count} celulares";

    private static string Names(IReadOnlyCollection<string> names)
    {
        var shown = names.Take(NamesShown);
        var rest = names.Count - NamesShown;
        return rest > 0 ? $"{string.Join(", ", shown)} e mais {rest}" : string.Join(", ", shown);
    }

    private static string SavedAtText(long savedAt) =>
        DateTimeOffset.FromUnixTimeSeconds(savedAt).ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
}
