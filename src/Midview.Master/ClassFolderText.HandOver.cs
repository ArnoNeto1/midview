namespace Midview.Master;

// ClassFolderText: what a take-over of the class folders says
// ("Guardar as pastas das turmas neste computador", ClassFolderHandOver).
public static partial class ClassFolderText
{
    // On a colleague's PC, for when the wrong teacher PC became the home.
    public const string TakeOverButton = "Guardar as pastas das turmas neste computador";

    // Why not while a lesson is on (what the room's Agents answer too): the button's tooltip, and a refusal.
    public const string EndLessonFirst = "Encerre a aula antes de passar as pastas.";

    // Asked before copying from the home PC: what its window lists (classes,
    // files, their size), where its copy goes on it (named after this PC,
    // thisPc), and, when this PC has classes of its own (ownClasses), where they go.
    public static string TakeOverQuestion(string home, int classes, int files, long bytes, string thisPc, bool ownClasses) =>
        $"Trazer as pastas das turmas do PC {home} para este computador?\n\n"
        + $"Vêm {Classes(classes)} com {Files(files)} ({Size(bytes)}), e este computador passa a guardar as pastas. "
        + $"O PC {home} guarda a cópia dele à parte, na pasta \"Turmas (passadas para {thisPc})\"."
        + (ownClasses ? "\n\nAs turmas que já estão neste computador ficam guardadas à parte, na pasta \"Turmas (antes de …)\"." : string.Empty);

    // Asked when the home PC's Midview doesn't answer: its classes stay on it.
    public static string TakeOverUnreachableQuestion(string home) =>
        $"O PC {home} não responde. Guardar as pastas neste computador assim mesmo? As turmas que estão nele não vêm junto; "
        + "os arquivos que ainda estão nos PCs dos alunos vêm na próxima aula.";

    // While the copy runs (progress: what arrived of it so far), then while the room's PCs are told.
    public static string TakeOverProgress(string home, HandOverProgress progress) =>
        progress.InPlace ? $"As pastas do PC {home} chegaram. Avisando os PCs da sala..."
        : progress.TotalFiles == 0 ? $"Trazendo as pastas das turmas do PC {home}..."
        : $"Trazendo as pastas das turmas do PC {home}: {Count(progress.Files)} de {Files(progress.TotalFiles)} "
            + $"({Size(progress.Bytes)} de {Size(progress.TotalBytes)})...";

    public static string TakeOverUnreachableProgress(string home) => $"Guardando as pastas neste computador no lugar do PC {home}...";

    // After the copy: this PC keeps the folders now.
    public static string TookOver(int classes, int files) =>
        $"As pastas das turmas agora ficam neste computador: {Classes(classes)} com {Files(files)}.";

    // After an unreachable home's take-over: this PC's own classes (if any) are the ones now.
    public const string TookOverUnreachable = "As pastas das turmas agora ficam neste computador.";

    // aside: the folder this PC's own earlier classes went to.
    public static string OwnClassesAside(string aside) => $"As turmas que já estavam neste computador foram para a pasta \"{aside}\".";

    // The old home put its copy aside, in that folder (its own answer: "Turmas (passadas para PROF-2) (2)", say).
    public static string OldCopyAside(string home, string folder) => $"A cópia do PC {home} ficou nele, na pasta \"{folder}\".";

    // ...its classes went to that folder (null: a name that can't be shown),
    // but not all that goes with them, and couldn't come back.
    public static string OldCopyPartlyAside(string home, string? folder) =>
        $"A cópia do PC {home} ficou nele, {(folder is null ? "numa pasta à parte" : $"na pasta \"{folder}\"")}, "
        + "mas nem tudo foi para lá: confira as turmas dele.";

    // A take-over whose outcome can't be told yet: pcs (by name) may have taken
    // this PC as the home and don't answer. Nothing was put back or deleted:
    // the copy is in Turmas, this PC's own classes aside (ownAside; null: it
    // had none); when those PCs are back, the room's PCs settle which PC keeps the folders.
    public static string TakeOverUndecided(string home, IReadOnlyList<string> pcs, string? ownAside) =>
        $"Ainda não dá para saber qual computador guarda as pastas: {MayHaveTaken(pcs)}. "
        + $"As turmas copiadas do PC {home} estão na pasta Turmas deste computador"
        + (ownAside is null ? "" : $", e as que já estavam aqui estão na pasta \"{ownAside}\"")
        + $". Nada foi apagado: quando {(pcs.Count == 1 ? "esse PC voltar" : "esses PCs voltarem")}, "
        + "os PCs da sala decidem qual computador guarda as pastas.";

    // ...an unreachable home's take-over: this PC's own classes stay as they are.
    public static string TakeOverUnreachableUndecided(IReadOnlyList<string> pcs) =>
        $"Ainda não dá para saber qual computador guarda as pastas: {MayHaveTaken(pcs)}. "
        + $"As turmas deste computador continuam onde estão. Quando {(pcs.Count == 1 ? "esse PC voltar" : "esses PCs voltarem")}, "
        + "os PCs da sala decidem qual computador guarda as pastas.";

    // An undecided take-over the old home took back (Ruling 21): the folders
    // stay on it, because pcs didn't confirm; this PC is as it was.
    public static string TakeOverTakenBack(string home, IReadOnlyList<string> pcs) =>
        $"As pastas das turmas continuam no PC {home}: "
        + (pcs.Count == 1 ? $"o PC {PcNames(pcs)} não confirmou a troca" : $"os PCs {PcNames(pcs)} não confirmaram a troca")
        + ". Tente de novo.";

    // Built on the old home: it isn't the home any more, so it doesn't take the room back.
    public static string NotTheHomeAnyMore(string pc) => $"O PC {pc} não guarda mais as pastas das turmas.";

    // ...the take-over given up has a generation from too far ahead of its clock.
    public static string TakeBackFromTooFarAhead(string pc) =>
        $"O PC {pc} não pegou as pastas de volta: a data da troca desfeita está adiantada demais em relação ao relógio dele.";

    // ...it couldn't start in time (the room's PCs slow to answer, say).
    public static string TakeBackTooSlow(string pc) =>
        $"O PC {pc} demorou demais para conferir os PCs da sala e não pegou as pastas de volta.";

    // "o PC LAB-03 pode ter aceitado este computador e não responde agora".
    private static string MayHaveTaken(IReadOnlyList<string> pcs) => pcs.Count == 1
        ? $"o PC {PcNames(pcs)} pode ter aceitado este computador e não responde agora"
        : $"os PCs {PcNames(pcs)} podem ter aceitado este computador e não respondem agora";

    // At most five names, then how many more.
    private static string PcNames(IReadOnlyList<string> pcs) =>
        string.Join(", ", pcs.Take(5)) + (pcs.Count > 5 ? $" e mais {Count(pcs.Count - 5)}" : string.Empty);

    // ...in a folder whose name it didn't give in a way that can be shown.
    public static string OldCopyAsideUnnamed(string home) => $"A cópia do PC {home} ficou nele, numa pasta à parte.";

    // files: how many files of the old home's class folders weren't copied
    // (what its store doesn't list, which the PCs never get either), so they
    // are only in its copy, in that folder there (null: one whose name can't be shown).
    public static string LeftOnTheOldHome(string home, int files, string? folder) =>
        (files == 1 ? "1 arquivo ficou" : $"{Count(files)} arquivos ficaram")
        + (folder is null ? $" só no PC {home}, numa pasta à parte" : $" só na pasta \"{folder}\" do PC {home}")
        + " (arquivos ocultos, grandes demais, com nomes que não servem ou fora de uma turma não passam para outro computador).";

    // The old home stood down with no classes to put aside.
    public static string OldHadNoClasses(string home) => $"O PC {home} não tinha turmas para guardar à parte.";

    // Why the old home's copy couldn't go aside, when it didn't say.
    public const string UnknownReason = "motivo desconhecido";

    // Built on the old home (pc: its own name) and shown on the new one: a
    // lesson is on there (some of its PCs still follow it), so it goes on
    // keeping the folders until that lesson ends.
    public static string LetsGoAfterTheLesson(string pc, string turma) =>
        $"O PC {pc} está com a aula do {turma} em andamento: ele deixa de guardar as pastas quando essa aula acabar, "
        + "e a cópia dele continua na pasta Turmas dele.";

    // Built on the old home and shown on the new one: the room's PCs don't keep newHome as the home.
    public static string NotTheHomeYet(string newHome, string pc) =>
        $"Os PCs da sala ainda não mostram o PC {newHome} como o que guarda as pastas; a cópia do PC {pc} continua na pasta Turmas dele.";

    // A take-over that broke off (a mistake in the code, reason ErrorText's words)
    // after the copy went in place, while the room's PCs were being told: which
    // PC keeps the folders isn't known here.
    public static string TakeOverBrokeOffClaiming(string home, string reason) =>
        $"A troca das pastas com o PC {home} parou no meio ({reason}): as turmas dele já estão neste computador, "
        + "mas os PCs da sala podem não ter sido avisados. Feche e abra esta janela para ver qual PC guarda as pastas.";

    // ...after the room's PCs took this PC as the home, while the old home was being told.
    public static string TakeOverBrokeOffTellingTheOldHome(string home, string reason) =>
        $"As pastas das turmas agora ficam neste computador, mas a troca parou ao avisar o PC {home} ({reason}): "
        + "a cópia dele pode continuar na pasta Turmas dele.";

    // ...an unreachable home's take-over, while the room's PCs were being told.
    public static string TakeOverUnreachableBrokeOff(string reason) =>
        $"A troca das pastas parou enquanto os PCs da sala eram avisados ({reason}). Feche e abra esta janela para ver qual PC guarda as pastas.";

    // The window couldn't tell how a take-over ended (reason: ErrorText's words).
    public static string TakeOverUnknown(string reason) =>
        $"Não foi possível saber como terminou a troca das pastas ({reason}). Feche e abra esta janela para ver qual PC guarda as pastas.";

    // A copy that an earlier take-over left half done, still there: nothing of it may be taken in.
    public static string StagingLeft(string folder) =>
        $"Uma cópia anterior que não terminou não pôde ser apagada. Apague a pasta \"{folder}\" e tente de novo.";

    // The room's PCs were busy with their students' folders and took no claim (ClassHomeClaim.BusyText).
    public const string PcsBusy = "Os PCs da sala estão ocupados agora. Tente de novo em instantes.";

    // A take-over that didn't stand (reason: why), whose moves couldn't all be
    // put back: what is where, as it is. copyStayed: the copy from the home is
    // still in this PC's Turmas folder; ownAside: the folder this PC's own
    // classes are still in (null: they aren't aside).
    public static string TakeOverNotPutBack(string home, string reason, bool copyStayed, string? ownAside)
    {
        var where = new List<string>();
        if (copyStayed)
        {
            where.Add("a pasta Turmas deste computador ficou com as turmas copiadas dele");
        }
        if (ownAside is not null)
        {
            where.Add($"as turmas que já estavam neste computador estão na pasta \"{ownAside}\"");
        }
        if (where.Count == 0)
        {
            where.Add("confira a pasta Turmas deste computador");
        }
        return $"As pastas não passaram do PC {home} para este computador, e nem tudo voltou ao lugar aqui: "
            + $"{string.Join(", e ", where)}. {Sentence(reason)}";
    }

    // What another PC said, as one line of at most max characters (it can say anything).
    public static string OneLine(string text, int max = 200)
    {
        var spaced = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var line = string.Join(' ', spaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= max)
        {
            return line;
        }
        var cut = char.IsHighSurrogate(line[max - 1]) ? max - 1 : max;
        return line[..cut] + "…";
    }

    // The teacher cancelled the copy ("Cancelar"), before anything changed.
    public const string CopyCancelled = "A cópia foi cancelada.";

    public static string TakeOverCancelling(string home) => $"Cancelando a cópia das pastas do PC {home}...";

    // The room's PCs keep another home than the one the window asked about.
    public static string HomeChanged(string confirmed, string now) =>
        $"As pastas agora ficam no PC {now}, não no PC {confirmed}. Confira e tente de novo.";

    // The old home stood down, but its copy couldn't go aside (reason: ErrorText's words, from that PC).
    public static string OldCopyNotMoved(string home, string reason) =>
        $"O PC {home} não conseguiu pôr a cópia dele à parte ({reason}): ela continua na pasta Turmas dele.";

    // The old home didn't say it took the change in: it will by itself (its Midview asks the PCs every 30 s).
    public static string OldHomeDidNotConfirm(string home) =>
        $"O PC {home} ainda não confirmou a troca: a cópia dele continua na pasta Turmas dele.";

    // A take-over that didn't happen; reason is made a whole sentence.
    public static string TakeOverFailed(string home, string reason) =>
        $"As pastas não foram trazidas do PC {home}; nada mudou. {Sentence(reason)}".TrimEnd();

    public static string TakeOverUnreachableFailed(string reason) =>
        $"As pastas não passaram para este computador; nada mudou. {Sentence(reason)}".TrimEnd();

    // Why a take-over didn't happen, from this PC's side.
    public static string CopyDropped(string home) => $"A cópia parou no meio: a conexão com o PC {home} caiu.";

    public static string CopyMismatch(string home) => $"O que veio do PC {home} não confere com as pastas das turmas.";

    public static string CopyNoRoom(long bytes, long freeBytes) =>
        $"Não há espaço neste computador: as pastas ocupam {Size(bytes)} e o disco tem {Size(freeBytes)} livres (é preciso sobrar 1 GB).";

    public static string CopyNotSaved(string reason) => $"Não foi possível gravar as pastas neste computador: {reason}";

    public const string TakeOverRunning = "As pastas já estão sendo trazidas para este computador.";

    // Each PC's line of a start on this PC while it takes the folders over (no lesson starts meanwhile).
    public const string TakeOverRunningHere = "este computador está recebendo as pastas das turmas; espere a troca terminar";

    // No PC of the room took this PC as the home (none answered).
    public const string NoPcTookIt = "Nenhum PC da sala respondeu.";

    // The home PC's Midview answers again since the window asked about it closed: asked again, its classes can come.
    public static string HomeAnswersAgain(string name) => $"O Midview do PC {name} voltou a responder. Tente de novo.";
}
