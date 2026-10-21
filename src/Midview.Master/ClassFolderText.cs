using System.Globalization;
using Midview.Protocol;

namespace Midview.Master;

// What the "Pasta da turma" window says (ClassFolderWindow), in the teacher's
// words and number format ("1,2 GB", "1.234 arquivos") whatever the PC's own.
public static class ClassFolderText
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // --- The class list ---

    // "38 arquivos · última aula 14/10", "vazia".
    public static string Summary(ClassSummary summary)
    {
        if (summary.Files == 0)
        {
            return "vazia";
        }
        var files = Files(summary.Files);
        return summary.LastLesson is { } day ? $"{files} · última aula {day.ToString("dd/MM", PtBr)}" : files;
    }

    // Asked before "Excluir": the files go to this PC's Recycle Bin.
    public static string DeleteQuestion(ClassSummary summary) => summary.Files switch
    {
        0 => $"Excluir a turma {summary.Turma}? Ela está vazia.",
        1 => $"Excluir a turma {summary.Turma}? O arquivo dela vai para a Lixeira deste PC.",
        _ => $"Excluir a turma {summary.Turma}? Os {Count(summary.Files)} arquivos dela vão para a Lixeira deste PC.",
    };

    public static string StartButton(string? turma) =>
        turma is null ? "Começar a aula" : $"Começar a aula do {turma}";

    // --- Sizes ---

    // Short: one decimal under ten ("1,2 GB", "180 GB"), in 1024s as Windows counts.
    public static string Size(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        return bytes switch
        {
            < 1024 => $"{bytes} bytes",
            < 1024 * 1024 => Scaled(bytes / KB, "KB"),
            < 1024 * 1024 * 1024 => Scaled(bytes / MB, "MB"),
            _ => Scaled(bytes / GB, "GB"),
        };
    }

    // --- During a lesson ---

    // "Aula começou às 13:30 · 214 arquivos (1,2 GB) · disco deste PC com 180 GB livres".
    // freeBytes long.MaxValue: not known (ClassStore.FreeBytes), left out.
    public static string Header(ClassLesson lesson, int files, long bytes, long freeBytes)
    {
        var started = DateTimeOffset.FromUnixTimeSeconds(lesson.StartedAt).LocalDateTime;
        var text = $"Aula começou às {started.ToString("HH:mm", PtBr)} · "
            + (files == 0 ? "nenhum arquivo ainda" : $"{Files(files)} ({Size(bytes)})");
        return freeBytes == long.MaxValue ? text : $"{text} · disco deste PC com {Size(freeBytes)} livres";
    }

    // Null while there is room (ClassStore.LowSpaceBytes or more).
    public static string? LowSpaceWarning(long freeBytes) =>
        freeBytes < ClassStore.LowSpaceBytes
            ? $"O disco deste computador está ficando cheio ({Size(freeBytes)} livres). Apague turmas antigas para continuar guardando arquivos."
            : null;

    // One PC in the window's compact list: a mark and the name, and why for a
    // PC that failed ("✗ LAB-09: Midview antigo").
    public static string State(PcStatus status)
    {
        var name = status.Machine.Name;
        return status.State switch
        {
            PcSyncState.InSync => $"✓ {name}",
            PcSyncState.Copying => $"⟳ {name}",
            PcSyncState.Offline => $"○ {name}",
            _ => status.Detail.Length > 0 ? $"✗ {name}: {status.Detail}" : $"✗ {name}",
        };
    }

    // What the mark means, for the PC's tooltip.
    public static string StateHint(PcStatus status) => status.State switch
    {
        PcSyncState.InSync => "Em dia",
        PcSyncState.Copying => status.Detail.Length > 0 ? $"Copiando: {status.Detail}" : "Copiando",
        PcSyncState.Offline => "Desligado: recebe ao ligar",
        _ => status.Detail,
    };

    // --- Ending a lesson ---

    // The line under each of the three choices.
    public static string EndChoiceHint(EndChoice choice, string turma, int todayFiles, int allFiles) => choice switch
    {
        EndChoice.Keep => $"Voltam para todos os PCs na próxima aula do {turma}.",
        EndChoice.DeleteToday => todayFiles switch
        {
            0 => "Nenhum arquivo chegou nesta aula; os das aulas anteriores ficam.",
            1 => "O arquivo desta aula sai; os das aulas anteriores ficam.",
            _ => $"Os {Count(todayFiles)} arquivos desta aula saem; os das aulas anteriores ficam.",
        },
        _ => allFiles switch
        {
            0 => "Não há arquivos na pasta da turma.",
            1 => "O arquivo sai do seu PC e de todos os PCs. Vai para a Lixeira deste PC.",
            _ => $"Os {Count(allFiles)} arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC.",
        },
    };

    // Asked before a lesson ends deleting files; null for "Manter", which asks nothing.
    public static string? EndQuestion(EndChoice choice, string turma, int todayFiles, int allFiles) => choice switch
    {
        EndChoice.Keep => null,
        EndChoice.DeleteToday => $"Encerrar a aula do {turma} e apagar os arquivos de hoje?\n\n"
            + EndChoiceHint(choice, turma, todayFiles, allFiles),
        _ => $"Encerrar a aula do {turma} e apagar tudo do {turma}?\n\n" + EndChoiceHint(choice, turma, todayFiles, allFiles),
    };

    // --- What each PC answered ---

    public static string Result(string name, bool ok, string detail) =>
        $"{(ok ? "✓" : "✗")} {name}" + (detail.Length > 0 ? $": {detail}" : string.Empty);

    // After a start (started) or an end: how many PCs took it. offline: how
    // many of the others were switched off, which get it from their poller
    // once they answer again; said only when that is all of them (any other
    // reason is in that PC's own line).
    public static string Outcome(bool started, string turma, int reached, int total, int offline)
    {
        var what = started ? $"A aula do {turma} começou" : $"A aula do {turma} foi encerrada";
        if (total == 0)
        {
            return what + ".";
        }
        if (reached == total)
        {
            return total == 1 ? $"{what} no único PC da sala." : $"{what} em todos os {total} PCs.";
        }
        var count = $"{what} em {reached} de {total} {(total == 1 ? "PC" : "PCs")}";
        if (reached + offline != total)
        {
            return count + ".";
        }
        return count + (started ? "; os desligados recebem a pasta ao ligar." : "; os desligados encerram ao ligar.");
    }

    // After "Trocar de turma…" ended the lesson.
    public const string ChooseNextClass = "Escolha a turma da próxima aula.";

    // --- Changes to the list ---

    public static string Renamed(string turma, string newName) => $"Turma {turma} agora se chama {newName}.";

    // The files went to this PC's Recycle Bin (ClassStore.Delete).
    public static string Deleted(ClassSummary summary) => summary.Files switch
    {
        0 => $"Turma {summary.Turma} excluída.",
        1 => $"Turma {summary.Turma} excluída. O arquivo dela está na Lixeira deste PC.",
        _ => $"Turma {summary.Turma} excluída. Os {Count(summary.Files)} arquivos dela estão na Lixeira deste PC.",
    };

    private static string Files(int count) => count == 1 ? "1 arquivo" : $"{Count(count)} arquivos";

    private static string Count(int count) => count.ToString("N0", PtBr);

    private static string Scaled(double value, string unit) =>
        $"{value.ToString(value < 10 ? "0.#" : "0", PtBr)} {unit}";
}
