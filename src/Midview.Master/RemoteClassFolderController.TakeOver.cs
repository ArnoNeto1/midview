using Midview.Protocol;

namespace Midview.Master;

// RemoteClassFolderController: "Guardar as pastas das turmas neste computador"
// (ClassFolderHandOver, plan decision 7, Ruling 17).
public sealed partial class RemoteClassFolderController
{
    // The class folders' home as the room says now (null: none known), for
    // the window to ask about it and to hand back to TakeOverAsync.
    public ClassHome? Home => CurrentHome();

    // The class folders to this PC, through handOver (this PC's). confirmed:
    // the home the teacher was asked about (Home then); when the room keeps
    // another PC as the home now, nothing is done and Detail says so. homeClosed:
    // whether the question was about a home that doesn't answer (its classes
    // don't come: the Agents only take this PC as the home) or one that does
    // (they are copied over first); when the home has closed or answered again
    // since, nothing is done either. Never with a lesson on. cancel: stops the
    // copy until it goes in place. One command at a time with the others; the
    // home's state is still asked for meanwhile (the copy is one of the
    // connections the home allows this PC, a state read another), and once
    // more at the end.
    public Task<HandOverResult> TakeOverAsync(
        ClassFolderHandOver handOver, ClassHome? confirmed, bool homeClosed, IProgress<HandOverProgress>? progress = null,
        HandOverCancel? cancel = null) => OneAtATime(async () =>
    {
        var home = CurrentHome();
        if (!IsSamePc(home, confirmed))
        {
            return new HandOverResult(false, home is null
                ? ClosedText
                : ClassFolderText.HomeChanged(NameOf(confirmed), NameOf(home)));
        }
        if (HomeClosed != homeClosed)
        {
            return new HandOverResult(false, homeClosed ? ClassFolderText.HomeAnswersAgain(Name) : ClosedText);
        }
        if (Lesson.IsOn)
        {
            return new HandOverResult(false, ClassFolderText.EndLessonFirst);
        }
        if (homeClosed)
        {
            return await handOver.TakeOverUnreachableAsync(Name).ConfigureAwait(false);
        }
        if (home is null)
        {
            return new HandOverResult(false, ClosedText);
        }

        var result = await handOver.TakeOverAsync(home, progress, cancel).ConfigureAwait(false);
        await PollOnceAsync().ConfigureAwait(false);
        return result;
    });

    // The same PC: its certificate and its computer name (its address may have changed).
    private static bool IsSamePc(ClassHome? a, ClassHome? b) =>
        a is null ? b is null
        : b is not null
            && string.Equals(a.Thumbprint, b.Thumbprint, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    private static string NameOf(ClassHome? home) => home?.Name is { Length: > 0 } name ? name : ClassHome.UnnamedText;
}
