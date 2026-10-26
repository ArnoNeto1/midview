namespace Midview.Master;

// Which mode ClassFolderRole acts on, from what each refresh of
// ClassHomeTracker reads (ClassHomeReading). A change counts only once
// Readings readings in a row say it: the same mode and, for Remote, the same
// home PC; one reading can be wrong (this PC's class-home.json unreadable for
// a moment shows it, renamed, as another PC: Task 3's note). The first
// reading that says Local is taken at once: a Master starts with nothing to
// undo (no host open, no lesson handed out); and so is one that names this PC
// as the home, with this PC's file read: the Agents' own word, never a reading
// gone wrong (this PC took the folders over, and its window is its own at
// once). With the file unread, stragglers naming this PC could outvote the
// newer home it saw: two readings, as any change. Remote is never
// taken from one, unless the role is sure of it (this PC handed the folders
// over itself).
//
// A change the role couldn't make (a lesson of this PC's still on) is asked
// for again at every reading that goes on saying it; acting Local meanwhile,
// with nothing decided before, doesn't start that run over. Not thread-safe:
// the role takes its readings one at a time.
internal sealed class ClassHomeConfirmation
{
    public const int Readings = 2;

    // The change the last readings say, and how many of them in a row.
    private (ClassFolderMode Mode, string Home)? _seen;
    private int _count;

    // The mode acted on; null before the first.
    public ClassFolderMode? Acted { get; private set; }

    // This reading began a change, or went on with one, not confirmed yet:
    // another reading soon would settle it.
    public bool Confirming { get; private set; }

    // The mode to act on now, or null: none (the one acted on, or a change
    // not confirmed yet: Confirming). sure: the change is known true (this PC
    // handed the folders over itself, and the reading says Remote): at once.
    public ClassFolderMode? Take(ClassHomeReading reading, bool sure = false)
    {
        Confirming = false;
        if (reading.Mode == Acted)
        {
            _seen = null;
            _count = 0;
            return null;
        }
        if (reading.Mode == ClassFolderMode.Local && (Acted is null || (reading.Home is not null && reading.FileRead)))
        {
            return ClassFolderMode.Local;
        }

        var seen = (reading.Mode, HomeOf(reading));
        _count = _seen == seen ? _count + 1 : 1;
        _seen = seen;
        if (_count >= Readings || sure)
        {
            return reading.Mode;
        }
        Confirming = true;
        return null;
    }

    // The role now acts in mode: a run of readings that said it is over; one
    // that says the other mode goes on.
    public void Acting(ClassFolderMode mode)
    {
        Acted = mode;
        if (_seen?.Mode == mode)
        {
            _seen = null;
            _count = 0;
        }
        Confirming = false;
    }

    // Which PC, for a Remote run (its address may change between readings).
    private static string HomeOf(ClassHomeReading reading) =>
        reading.Mode == ClassFolderMode.Remote && reading.Home is { } home
            ? $"{home.Thumbprint.ToUpperInvariant()}\n{home.Name.ToUpperInvariant()}"
            : string.Empty;
}
