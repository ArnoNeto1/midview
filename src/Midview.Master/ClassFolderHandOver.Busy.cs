using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: what the Master goes by around a take-over.
//
// IsBusy: a take-over runs on this PC, a hand-over or a take-back runs for a
// colleague's PC, or a copy this PC sent hasn't settled yet. Not settled: the
// PC taking the folders over hasn't said how it went (RemoteHandOverDone, or
// RemoteTakeBack when it can't tell), and its answer must find this PC's host
// open even while the room's Agents already name it, for up to
// SettleWait. Meanwhile closing the Master would leave a take-over half done,
// and ClassFolderRole neither stands down nor closes the host. Once one is
// over, whatever it came to, Changed tells the role to ask the room which PC
// keeps the folders now.
//
// HandedOver: the PC this one handed the folders over to (it stood down for
// it), kept on file too, so a Master opened again remembers. It no longer
// counts once this PC takes the folders over again, or the room names this PC
// the home above the generation it handed them over at (SeenAsHome: an
// undecided take-over of its own the room later decided for it, say).
public sealed partial class ClassFolderHandOver
{
    // What the asides a take-over leaves beside the classes are named after:
    // "Turmas (antes de 20-10-2026 1330)", "Turmas (passadas para PROF-2)".
    internal const string BeforeWhyStart = "antes de ";

    internal const string PassedToWhyStart = "passadas para ";

    // How long the PC taking the folders over may take from the copy's end to
    // saying how it went: its claims and the Agents asked again after a lost
    // answer, each bounded by its network wait, and the lost answer's wait.
    internal static TimeSpan DefaultSettleWait => 2 * DefaultNetworkWait + DefaultLostAnswerWait;

    // Copies sent and not settled, by the thumbprint of the PC taking them:
    // which copy (a newer one to the same PC replaces it), and that PC's name
    // as it gave it (empty: none). Under _settling.
    private readonly object _settling = new();
    private readonly Dictionary<string, (long Copy, string Name)> _unsettled = new(StringComparer.OrdinalIgnoreCase);
    private long _copies;

    // The PC this one handed the folders over to (as the Agents recorded it,
    // at its generation then); null for none. Known: it says what the file
    // does (read, or written since), so the file isn't read again. Both under
    // _handedOverGate (a hand-over, a take-back, a take-over and the role's
    // readings each change it).
    private readonly object _handedOverGate = new();
    private ClassHome? _handedOverTo;
    private bool _handedOverKnown;

    // When a take-over, hand-over, take-back or copy last stopped keeping this PC busy (Environment.TickCount64).
    private long _quietSince;

    // Raised (on a background thread) once a take-over of this PC's is over,
    // once a colleague's PC said how its take-over went (or never did), and
    // once a copy settled by itself.
    public event Action? Changed;

    public bool IsBusy => IsRunning || AnyUnsettled();

    // A take-over runs on this PC, or a hand-over or a take-back for a
    // colleague's PC: closing the Master now would leave it half done.
    public bool IsRunning => Volatile.Read(ref _taking) == 1 || _giving.CurrentCount == 0;

    // The PCs (by the name each gave; empty: none) whose copy hasn't settled:
    // nothing runs here for them, this PC only waits for them to confirm.
    public IReadOnlyList<string> Unconfirmed
    {
        get
        {
            lock (_settling)
            {
                return _unsettled.Values.Select(copy => copy.Name).ToList();
            }
        }
    }

    // When the last of those ended (Environment.TickCount64; 0 before any): a
    // reading of the room begun before then says what the room said meanwhile
    // (ClassFolderRole doesn't go by it).
    public long QuietSince => Interlocked.Read(ref _quietSince);

    // This PC handed the folders over, and that still counts: the room keeps
    // another PC as the home, as no reading gone wrong would say (ClassFolderRole).
    public bool HandedOver => Volatile.Read(ref _handedOverTo) is not null;

    // DefaultSettleWait, or a test's.
    internal TimeSpan SettleWait { get; set; } = DefaultSettleWait;

    // The room names this PC the home at that generation (ClassFolderRole, from
    // its readings): a hand-over at an older one is behind it.
    public void SeenAsHome(long generation)
    {
        if (HandedOverStanding().To is { } to && generation > to.Generation)
        {
            _log($"The room names this PC the class folders' home again (generation {generation}): "
                + $"its hand-over to {to.Name} no longer counts.");
            KeepHandedOver(null);
        }
    }

    // The whole copy went to that PC (before its last answer goes, so it can't
    // claim the Agents first; name: as it gave it): not settled until it says
    // how it went, or SettleWait. It holds what Send began
    // (ClassFolderSync.BeginGiving: no lesson starts here), let go once it
    // settles, or once a newer copy to that PC replaces it.
    private void CopySent(string thumbprint, string name)
    {
        long copy;
        bool replaced;
        lock (_settling)
        {
            copy = ++_copies;
            replaced = _unsettled.ContainsKey(thumbprint);
            _unsettled[thumbprint] = (copy, name);
        }
        if (replaced)
        {
            _sync.EndGiving();
        }
        var wait = SettleWait;
        _ = Task.Run(async () =>
        {
            await Task.Delay(wait).ConfigureAwait(false);
            bool expired;
            lock (_settling)
            {
                expired = _unsettled.TryGetValue(thumbprint, out var still) && still.Copy == copy && _unsettled.Remove(thumbprint);
            }
            if (expired)
            {
                _log("A colleague's PC that took a copy of the class folders never said how its take-over went.");
                _sync.EndGiving();
                Quiet();
            }
        });
    }

    // That PC said how its take-over went (or a copy to it couldn't end): settled, whatever it said.
    private void Settled(string thumbprint)
    {
        bool settled;
        lock (_settling)
        {
            settled = _unsettled.Remove(thumbprint);
        }
        if (settled)
        {
            _sync.EndGiving();
        }
        Quiet();
    }

    // Something that kept this PC busy ended: from now on, readings count again, and the room is asked afresh.
    private void Quiet()
    {
        Interlocked.Exchange(ref _quietSince, Environment.TickCount64);
        RaiseChanged();
    }

    private bool AnyUnsettled()
    {
        lock (_settling)
        {
            return _unsettled.Count > 0;
        }
    }

    // Kept in memory and on file (memory covers a file that couldn't be written).
    private void KeepHandedOver(ClassHome? to)
    {
        lock (_handedOverGate)
        {
            Volatile.Write(ref _handedOverTo, to);
            _handedOverKnown = true;
            try
            {
                ClassHomeFileMaster.WriteHandedOverTo(to, _homePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"Which PC this one handed the class folders over to could not be saved: {ex.Message}");
            }
        }
    }

    // The hand-over that counts, as this run knows it or as the file has it
    // (null: none), the file read only until it once could be; Unread when it
    // can't be read just now.
    private (ClassHome? To, bool Unread) HandedOverStanding()
    {
        lock (_handedOverGate)
        {
            if (_handedOverKnown)
            {
                return (_handedOverTo, false);
            }
            try
            {
                Volatile.Write(ref _handedOverTo, ClassHomeFileMaster.ReadHandedOverTo(_homePath));
                _handedOverKnown = true;
                return (_handedOverTo, false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"Which PC this one handed the class folders over to could not be read: {ex.Message}");
                return (null, true);
            }
        }
    }

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
