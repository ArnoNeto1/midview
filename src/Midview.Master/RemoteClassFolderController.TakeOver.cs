namespace Midview.Master;

// RemoteClassFolderController: "Guardar as pastas das turmas neste computador"
// (ClassFolderHandOver, plan decision 7).
public sealed partial class RemoteClassFolderController
{
    // The class folders to this PC, through handOver (this PC's). homeClosed:
    // whether the window asked the teacher about a home that doesn't answer
    // (its classes don't come: the Agents only take this PC as the home) or
    // one that does (they are copied over first); when the home has closed or
    // answered again since, nothing is done and Detail says so. Never with a
    // lesson on. One command at a time with the others; the home's state is
    // still asked for meanwhile (the copy is one of the connections the home
    // allows this PC, a state read another), and once more at the end.
    public Task<HandOverResult> TakeOverAsync(
        ClassFolderHandOver handOver, bool homeClosed, IProgress<HandOverProgress>? progress = null) => OneAtATime(async () =>
    {
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
        if (CurrentHome() is not { } home)
        {
            return new HandOverResult(false, ClosedText);
        }

        var result = await handOver.TakeOverAsync(home, progress).ConfigureAwait(false);
        await PollOnceAsync().ConfigureAwait(false);
        return result;
    });
}
