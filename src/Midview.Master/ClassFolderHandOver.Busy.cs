namespace Midview.Master;

// ClassFolderHandOver: what the Master goes by around a take-over (Task 7).
// While one runs, here or for a colleague's PC (taking the folders over,
// standing down for it, taking the room back), closing the Master would leave
// it half done (a take-over goes on for minutes after its copy is in place),
// so the Master waits for IsBusy to go. Once one is over, whatever it came
// to, Changed tells ClassFolderRole to ask the room which PC keeps the
// folders now, so both PCs swap modes without waiting for the next reading.
public sealed partial class ClassFolderHandOver
{
    // What the asides a take-over leaves beside the classes are named after:
    // "Turmas (antes de 20-10-2026 1330)", "Turmas (passadas para PROF-2)".
    internal const string BeforeWhyStart = "antes de ";

    internal const string PassedToWhyStart = "passadas para ";

    // Raised (on a background thread) once a take-over of this PC's is over,
    // or once this PC stood down for a colleague's or took the room back.
    public event Action? Changed;

    // A take-over runs on this PC, or a hand-over or a take-back for a colleague's.
    public bool IsBusy => Volatile.Read(ref _taking) == 1 || _giving.CurrentCount == 0;

    // In this run, this PC handed the folders over to a colleague's PC (it
    // stood down for it) and hasn't taken them over since: the room keeps
    // another PC as the home, as no reading gone wrong would say (ClassFolderRole).
    public bool HandedOver => _handedOver;

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log($"A Changed handler failed: {ex}");
        }
    }
}
