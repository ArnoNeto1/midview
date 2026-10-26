using System.Globalization;
using System.Text.RegularExpressions;
using Midview.Protocol;

namespace Midview.Master;

// What the "Pasta da turma" window says (ClassFolderWindow), in the teacher's
// words and number format ("1,2 GB", "1.234 arquivos") whatever the PC's own.
// The texts of a take-over (ClassFolderHandOver) are in ClassFolderText.HandOver.cs.
public static partial class ClassFolderText
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

    // home, in the texts that follow: the computer name of the PC that keeps the
    // class folders when the window is on another teacher PC
    // (IClassFolderController.HomeName), whose disk and Recycle Bin they are
    // about; null on that PC itself ("deste PC").

    // Asked before "Excluir": the files go to the Recycle Bin of the PC that
    // keeps them, unless they are more than it holds (tooBigForTheBin,
    // RecycleBinRoom): then Windows deletes them for good, and the question says so.
    public static string DeleteQuestion(ClassSummary summary, bool tooBigForTheBin, string? home = null) => (summary.Files, tooBigForTheBin) switch
    {
        (0, _) => $"Excluir a turma {summary.Turma}? Ela está vazia.",
        (1, false) => $"Excluir a turma {summary.Turma}? O arquivo dela vai para a Lixeira {Of(home)}.",
        (_, false) => $"Excluir a turma {summary.Turma}? Os {Count(summary.Files)} arquivos dela vão para a Lixeira {Of(home)}.",
        (1, true) => $"Excluir a turma {summary.Turma}? O arquivo dela ({Size(summary.Bytes)}) não cabe na Lixeira {Of(home)} e será apagado de vez.",
        _ => $"Excluir a turma {summary.Turma}? Os {Count(summary.Files)} arquivos dela ({Size(summary.Bytes)}) não cabem na Lixeira {Of(home)} e serão apagados de vez.",
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
    public static string Header(ClassLesson lesson, int files, long bytes, long freeBytes, string? home = null)
    {
        var started = DateTimeOffset.FromUnixTimeSeconds(lesson.StartedAt).LocalDateTime;
        var text = $"Aula começou às {started.ToString("HH:mm", PtBr)} · "
            + (files == 0 ? "nenhum arquivo ainda" : $"{Files(files)} ({Size(bytes)})");
        return freeBytes == long.MaxValue ? text : $"{text} · disco {Of(home)} com {Size(freeBytes)} livres";
    }

    // Null while there is room (ClassStore.LowSpaceBytes or more).
    public static string? LowSpaceWarning(long freeBytes, string? home = null) =>
        freeBytes < ClassStore.LowSpaceBytes
            ? $"O disco {(home is null ? "deste computador" : $"do PC {home}")} está ficando cheio ({Size(freeBytes)} livres). Apague turmas antigas para continuar guardando arquivos."
            : null;

    // When the numbers of the lesson's files couldn't be read for an end that deletes some.
    public static string LessonFilesUnreadable(string? home = null) =>
        $"A pasta da turma {Of(home)} não pôde ser lida agora. Tente de novo em instantes.";

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

    // The line under each of the three choices. tooBigForTheBin: the files
    // that choice deletes are more than the Recycle Bin of the PC that keeps
    // them holds (RecycleBinRoom), so Windows deletes them for good: the line
    // says so instead of promising the Recycle Bin.
    public static string EndChoiceHint(
        EndChoice choice, string turma, int todayFiles, int allFiles, bool tooBigForTheBin, string? home = null) => choice switch
    {
        EndChoice.Keep => $"Voltam para todos os PCs na próxima aula do {turma}.",
        EndChoice.DeleteToday => (todayFiles, tooBigForTheBin) switch
        {
            (0, _) => "Nenhum arquivo chegou nesta aula; os das aulas anteriores ficam.",
            (1, false) => "O arquivo desta aula sai; os das aulas anteriores ficam.",
            (_, false) => $"Os {Count(todayFiles)} arquivos desta aula saem; os das aulas anteriores ficam.",
            (1, true) => $"O arquivo desta aula sai; os das aulas anteriores ficam. Ele não cabe na Lixeira {Of(home)}: será apagado de vez.",
            _ => $"Os {Count(todayFiles)} arquivos desta aula saem; os das aulas anteriores ficam. Os desta aula não cabem na Lixeira {Of(home)}: serão apagados de vez.",
        },
        _ => (allFiles, tooBigForTheBin) switch
        {
            (0, _) => "Não há arquivos na pasta da turma.",
            (1, false) => $"O arquivo sai {Yours(home)} e de todos os PCs. Vai para a Lixeira {Of(home)}.",
            (_, false) => $"Os {Count(allFiles)} arquivos saem {Yours(home)} e de todos os PCs. Vão para a Lixeira {Of(home)}.",
            (1, true) => $"O arquivo sai {Yours(home)} e de todos os PCs. Não cabe na Lixeira {Of(home)}: será apagado de vez.",
            _ => $"Os {Count(allFiles)} arquivos saem {Yours(home)} e de todos os PCs. Não cabem na Lixeira {Of(home)}: serão apagados de vez.",
        },
    };

    // Asked before a lesson ends deleting files; null for "Manter", which asks nothing.
    public static string? EndQuestion(
        EndChoice choice, string turma, int todayFiles, int allFiles, bool tooBigForTheBin, string? home = null) => choice switch
    {
        EndChoice.Keep => null,
        EndChoice.DeleteToday => $"Encerrar a aula do {turma} e apagar os arquivos de hoje?\n\n"
            + EndChoiceHint(choice, turma, todayFiles, allFiles, tooBigForTheBin, home),
        _ => $"Encerrar a aula do {turma} e apagar tudo do {turma}?\n\n"
            + EndChoiceHint(choice, turma, todayFiles, allFiles, tooBigForTheBin, home),
    };

    // --- What each PC answered ---

    public static string Result(string name, bool ok, string detail) =>
        $"{(ok ? "✓" : "✗")} {name}" + (detail.Length > 0 ? $": {detail}" : string.Empty);

    // A PC busy with its student's folders when a start or an end reached it:
    // this PC's poller hands it the lesson in a moment (ThumbnailPoller.DefaultBusyRetryInterval).
    public const string LessonBusy = "ocupado agora; o Midview tenta de novo em instantes";

    // The end of a lesson this PC held for the room while another teacher PC
    // keeps the folders, for a PC that wasn't in it (it follows that PC's
    // lesson, or none): nothing to end there.
    public const string NotInThisLesson = "não estava nesta aula";

    // After a start (started) or an end: how many PCs took it. offline: how
    // many of the others were switched off, which get it from their poller
    // once they answer again; said only when that is all of them (any other
    // reason is in that PC's own line). closedHome: an end made straight on
    // the PCs because that home PC's Midview was closed; nothing hands it to
    // the PCs that were off until that Midview opens (or the day ends, when
    // each PC ends the lesson by itself).
    public static string Outcome(bool started, string turma, int reached, int total, int offline, string? closedHome = null)
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
        return count + (started ? "; os desligados recebem a pasta ao ligar."
            : closedHome is not null ? $"; os desligados continuam na aula até o Midview do PC {closedHome} abrir ou o dia acabar."
            : "; os desligados encerram ao ligar.");
    }

    // After "Trocar de turma…" ended the lesson.
    public const string ChooseNextClass = "Escolha a turma da próxima aula.";

    // A start that didn't happen because another teacher PC keeps the class
    // folders (each PC's line says which), or this PC couldn't tell.
    public static string NotStarted(string turma) => $"A aula do {turma} não começou.";

    // A start or an end that failed as a whole; reason is ErrorText.Describe's.
    public static string StartFailed(string turma, string reason) => $"Não foi possível começar a aula do {turma}: {reason}";

    public static string EndFailed(string turma, string reason) => $"Não foi possível encerrar a aula do {turma}: {reason}";

    // --- Refusals (a window that showed something already changed: the other teacher PC's, say) ---

    public const string ClassGone = "Essa turma não existe mais.";

    public const string LessonClassNotRenamed = "A turma da aula em andamento não pode ser renomeada.";

    public const string LessonClassNotDeleted = "A turma da aula em andamento não pode ser excluída.";

    // A start asked for while a lesson is on.
    public static string LessonAlreadyOn(string turma) => $"A aula do {turma} está em andamento: encerre-a antes de começar outra.";

    // An end asked for a lesson that is no longer on.
    public static string LessonAlreadyOver(string turma) => $"A aula do {turma} já foi encerrada.";

    // --- The class folders on another teacher PC (RemoteClassFolderController) ---

    // Above the window on a colleague's PC.
    public static string RemoteBanner(string name) => $"As pastas das turmas ficam no PC {name}. O que você fizer aqui acontece nele.";

    // The home PC's Midview doesn't answer; then only "Encerrar a aula" works (keeping the files).
    public static string HomeClosed(string name) => $"O Midview do PC {name}, que guarda as pastas das turmas, está fechado.";

    // Off, or on a new network address (which its Midview gives the room's
    // PCs within a poll), before blaming the firewall rule.
    public const string HomeClosedHint =
        "O PC pode estar desligado ou ter mudado de endereço na rede (um endereço novo se acerta sozinho em instantes). "
        + "Se ele está ligado e aberto, o Midview desse PC precisa ser instalado de novo para aceitar este computador "
        + "(regra do firewall).";

    // The home took the connection but gave no answer (it may have done what was asked).
    public static string HomeNoAnswer(string name) => $"O Midview do PC {name} não respondeu. Tente de novo em instantes.";

    // EndFailed's reason when, with the home closed, no PC of the room said which lesson it follows.
    public const string NoPcAnswered = "nenhum PC da sala respondeu";

    // The PC at the home's address answered with another certificate than the one the room knows for it.
    public static string HomeOtherIdentity(string name) => $"O PC {name} respondeu com outra identidade; ele foi reinstalado?";

    // Lines the home builds about its own disk ("neste computador"), which a
    // colleague's PC shows saying "no PC {home}" (Relayed). A PC's line when
    // the class's folder there can't be read (ClassFolderSync's round):
    public static string ClassFolderUnreadable(string reason, string? home = null) =>
        $"a pasta da turma {Here(home)} não pôde ser lida: {reason}";

    // ...and each PC's line of a start when what that PC knows of the class
    // folders' home (ClassHomeFileMaster) can't be read.
    public static string HomeFileUnreadable(string reason, string? home = null) =>
        $"não foi possível ler, {Here(home)}, qual PC guarda as pastas das turmas: {reason}";

    // A line the home built about itself, as a colleague's PC shows it (home:
    // the home's name); any other line as it is. Also a file a round couldn't
    // bring in for the home's full disk: "a.docx (não há espaço livre
    // suficiente neste computador)", and maybe " e mais 2" (Ruling 23), with
    // nothing else after it.
    public static string Relayed(string detail, string home)
    {
        foreach (var said in new Func<string, string?, string>[] { ClassFolderUnreadable, HomeFileUnreadable })
        {
            var here = said(string.Empty, null);
            if (detail.StartsWith(here, StringComparison.Ordinal))
            {
                return said(detail[here.Length..], home);
            }
        }

        var full = $"({FileCollector.NoSpaceText})";
        var at = detail.LastIndexOf(full, StringComparison.Ordinal);
        var after = at < 0 ? string.Empty : detail[(at + full.Length)..];
        return at >= 0 && (after.Length == 0 || MoreFiles.IsMatch(after))
            ? detail[..at] + $"({FileCollector.NoSpaceText.Replace(Here(null), Here(home), StringComparison.Ordinal)})" + after
            : detail;
    }

    // What a round's failure line ends with when more files failed (ClassFolderSync.Round.cs).
    private static readonly Regex MoreFiles = new(@"^ e mais [0-9]+\z", RegexOptions.CultureInvariant);

    // --- Changes to the list ---

    public static string Renamed(string turma, string newName) => $"Turma {turma} agora se chama {newName}.";

    // A deletion that failed as a whole; reason is ErrorText.Describe's.
    public static string DeleteFailed(string reason) => "Não foi possível excluir a turma: " + reason;

    // The files went to the Recycle Bin of the PC that keeps them (ClassStore.
    // Delete), or for good when they were more than it holds (tooBigForTheBin).
    public static string Deleted(ClassSummary summary, bool tooBigForTheBin, string? home = null) => (summary.Files, tooBigForTheBin) switch
    {
        (0, _) => $"Turma {summary.Turma} excluída.",
        (1, false) => $"Turma {summary.Turma} excluída. O arquivo dela está na Lixeira {Of(home)}.",
        (_, false) => $"Turma {summary.Turma} excluída. Os {Count(summary.Files)} arquivos dela estão na Lixeira {Of(home)}.",
        (1, true) => $"Turma {summary.Turma} excluída. O arquivo dela foi apagado de vez: não cabia na Lixeira {Of(home)}.",
        _ => $"Turma {summary.Turma} excluída. Os {Count(summary.Files)} arquivos dela foram apagados de vez: não cabiam na Lixeira {Of(home)}.",
    };

    // "deste PC", or "do PC PROF-1" from another teacher PC.
    private static string Of(string? home) => home is null ? "deste PC" : $"do PC {home}";

    // "do seu PC", or "do PC PROF-1" from another teacher PC.
    private static string Yours(string? home) => home is null ? "do seu PC" : $"do PC {home}";

    // "neste computador", or "no PC PROF-1" from another teacher PC.
    private static string Here(string? home) => home is null ? "neste computador" : $"no PC {home}";

    private static string Files(int count) => count == 1 ? "1 arquivo" : $"{Count(count)} arquivos";

    private static string Classes(int count) => count switch
    {
        0 => "nenhuma turma",
        1 => "1 turma",
        _ => $"{Count(count)} turmas",
    };

    // A detail made a sentence: ending with a full stop (an Agent's refusal may not).
    private static string Sentence(string text) =>
        text.Length == 0 || text.EndsWith('.') || text.EndsWith('?') || text.EndsWith('!') ? text : text + ".";

    private static string Count(int count) => count.ToString("N0", PtBr);

    private static string Scaled(double value, string unit) =>
        $"{value.ToString(value < 10 ? "0.#" : "0", PtBr)} {unit}";
}
