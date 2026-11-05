using System.Globalization;
using Microsoft.Win32;

namespace Midview.Protocol;

// The one rule that puts back what Midview pointed at a lesson's class folder.
// The Session runs it when a lesson ends; the Agent, as SYSTEM, runs the same
// code on uninstall from a state file the student can edit, so every setting is
// checked against the allowlist and against what is in the registry now.
public static class ClassSaveLocationsUndo
{
    // Returns one Portuguese line per setting that could not be put back; never throws.
    public static IReadOnlyList<string> Run(RegistryKey software, IEnumerable<SavedSetting> settings, Action<string>? setDownloads = null)
    {
        var problems = new List<string>();
        if (settings is null)
            return problems;

        // The enumeration is driven by hand: an enumerator can throw too (a state
        // file read lazily), and that must end the undo with a line, not escape.
        IEnumerator<SavedSetting>? enumerator = null;
        try
        {
            enumerator = settings.GetEnumerator();
            while (enumerator.MoveNext())
                UndoOne(software, enumerator.Current, setDownloads, problems);
        }
        catch (Exception ex)
        {
            problems.Add($"Não foi possível ler a lista de configurações: {ErrorText.Describe(ex)}");
        }
        finally
        {
            try
            {
                enumerator?.Dispose();
            }
            catch (Exception)
            {
                // Nothing left to undo, and the caller must not see this.
            }
        }
        return problems;
    }

    private static void UndoOne(RegistryKey software, SavedSetting? setting, Action<string>? setDownloads, List<string> problems)
    {
        try
        {
            if (!ClassSaveTargets.IsAllowed(setting))
                return;
            var current = ClassSaveTargets.ReadEncoded(software, setting!.Key, setting.Name, out var kind);
            // A value the student changed since is theirs now.
            if (!ClassSaveTargets.SameValue(current, kind, setting))
                return;
            PutBack(software, setting, setDownloads, problems);
        }
        catch (Exception ex)
        {
            // Anything a student can provoke (denied access, a registry link)
            // must cost only this setting, not the rest of the undo.
            problems.Add($"Não foi possível devolver {Describe(setting)}: {ErrorText.Describe(ex)}");
        }
    }

    private static string Describe(SavedSetting? setting) =>
        setting is null ? "uma configuração" : IsDownloads(setting) ? "Downloads" : setting.Name;

    private static bool IsDownloads(SavedSetting s) =>
        string.Equals(s.Key, ClassSaveTargets.DownloadsKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(s.Name, ClassSaveTargets.DownloadsName, StringComparison.OrdinalIgnoreCase);

    private static void PutBack(RegistryKey software, SavedSetting setting, Action<string>? setDownloads, List<string> problems)
    {
        string? callbackError = null;
        if (IsDownloads(setting) && setting.Before is not null && setDownloads is not null)
        {
            // The shell call only tells Explorer; it may store the path expanded,
            // so the raw write below is what makes the stored text exact.
            try
            {
                setDownloads(setting.Before);
            }
            catch (Exception ex)
            {
                callbackError = ErrorText.Describe(ex);
            }
        }

        ClassSaveTargets.WriteEncoded(software, setting.Key, setting.Name, setting.Kind, setting.Before);

        if (!IsDownloads(setting) && setting.Before is null && IsMruEntry(setting))
            RemoveFromOrder(software, setting.Name);

        if (callbackError is not null)
            problems.Add($"Não foi possível avisar o Windows sobre a pasta Downloads: {callbackError}");
    }

    private static bool IsMruEntry(SavedSetting s) =>
        string.Equals(s.Key, ClassSaveTargets.MruKey, StringComparison.OrdinalIgnoreCase);

    // Only a MRUListEx that reads cleanly is edited; otherwise the dialog's
    // history stays as it is.
    private static void RemoveFromOrder(RegistryKey software, string name)
    {
        if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return;
        var encoded = ClassSaveTargets.ReadEncoded(software, ClassSaveTargets.MruKey, LastVisitedMru.OrderName, out var kind);
        if (encoded is null || kind != RegistryValueKind.Binary)
            return;
        var order = LastVisitedMru.ParseOrder(Convert.FromBase64String(encoded));
        if (order is null || !order.Remove(number))
            return;
        ClassSaveTargets.WriteEncoded(software, ClassSaveTargets.MruKey, LastVisitedMru.OrderName,
            RegistryValueKind.Binary, Convert.ToBase64String(LastVisitedMru.FormatOrder(order)));
    }
}
