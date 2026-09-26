using System.Globalization;
using Midview.Protocol;

namespace Midview.Master;

// How the update window words a machine's state, kept apart from the window
// so it can be tested.
public static class UpdateText
{
    // "2026-09-26 19:40 a1b2c3d" (the manifest's label) as "26/09 19:40
    // (a1b2c3d)"; anything else as it is.
    public static string ShortBuild(string? build)
    {
        if (build is null)
        {
            return "desconhecida";
        }
        var parts = build.Split(' ', 3);
        if (parts.Length >= 2 && DateTime.TryParseExact($"{parts[0]} {parts[1]}", "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var built))
        {
            var date = built.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
            return parts.Length == 3 ? $"{date} ({parts[2]})" : date;
        }
        return build;
    }

    // Labels start with the build's date and time, so they sort in time.
    public static bool IsNewer(string? installed, string package) =>
        installed is not null && installed.Length >= 16 && package.Length >= 16
        && string.CompareOrdinal(installed[..16], package[..16]) > 0;

    // One line for "Verificar versões" (and the check before updating).
    public static string DescribeStatus(StatusResult status, string packageFingerprintHex, string packageBuild)
    {
        if (status.OldAgent || status.Report is null)
        {
            return "✗ Midview anterior à atualização pela rede (atualize uma vez com o pendrive)";
        }

        var report = status.Report;
        var busy = report.Attempt is { } attempt && !UpdateStates.IsFinal(attempt.State)
            ? " — há uma atualização em andamento nela"
            : string.Empty;
        if (!report.Ready)
        {
            return $"• calculando a versão instalada; verifique de novo em instantes{busy}";
        }
        if (string.Equals(report.Fingerprint, packageFingerprintHex, StringComparison.OrdinalIgnoreCase))
        {
            return $"✓ já nesta versão ({ShortBuild(report.Build)}){busy}";
        }
        return IsNewer(report.Build, packageBuild)
            ? $"⚠ instalada: {ShortBuild(report.Build)}, mais nova que o pacote ({ShortBuild(packageBuild)}){busy}"
            : $"• instalada: {ShortBuild(report.Build)} → pacote: {ShortBuild(packageBuild)}{busy}";
    }

    // While polling one attempt: what the machine is doing.
    public static string DescribeProgress(string state) => state switch
    {
        UpdateStates.Receiving => "recebendo...",
        UpdateStates.Verifying => "conferindo o pacote...",
        UpdateStates.Applying => "trocando de versão...",
        UpdateStates.Provisional => "reiniciando na versão nova...",
        _ => state,
    };
}
