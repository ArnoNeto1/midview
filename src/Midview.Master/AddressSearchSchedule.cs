namespace Midview.Master;

// When the Master looks for the room on the network by itself, the way
// "Procurar na rede" does, to find a machine that came back at another
// address: a phone on another Wi-Fi, a PC that got another address from the
// router (after the whole lab is woken at once, some only get theirs minutes
// later). Only while a machine such a search can move (Roster.MovesBySearch)
// has been offline for OfflineFor; never in the first FirstAfter, while the
// first polls are still on their way; then at most every Interval. Searches
// that move nothing for the same offline machines wait twice as long each
// time, up to LongestInterval (PCs simply switched off stay offline all
// class long); a machine newly offline brings it back to Interval.
// Machines are named by their Agent thumbprint. Only used on the UI thread.
public sealed class AddressSearchSchedule
{
    public static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan OfflineFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan LongestInterval = TimeSpan.FromMinutes(12);

    private readonly DateTime _start;
    private readonly Dictionary<string, DateTime> _offlineSince = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lookedFor = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastSearch;
    private int _fruitless;

    public AddressSearchSchedule(DateTime start)
    {
        _start = start;
    }

    // Whether to search now. offline: the machines a search can move that
    // aren't answering right now; called every few seconds, it keeps when
    // each of them stopped answering.
    public bool Due(DateTime now, IEnumerable<string> offline)
    {
        var current = new HashSet<string>(offline, StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _offlineSince.Keys.Where(key => !current.Contains(key)).ToList())
        {
            _offlineSince.Remove(gone);
        }
        foreach (var key in current)
        {
            _offlineSince.TryAdd(key, now);
        }

        var missing = Missing(now);
        if (now - _start < FirstAfter || missing.Count == 0)
        {
            return false;
        }
        if (_lastSearch is not { } last)
        {
            return true;
        }
        return now - last >= (missing.Any(key => !_lookedFor.Contains(key)) ? Interval : Wait());
    }

    // A search ended (this schedule's or the teacher's own "Procurar na
    // rede"): moved is how many machines it found at a new address.
    public void Searched(DateTime now, int moved)
    {
        if (moved > 0 || Missing(now).Any(key => !_lookedFor.Contains(key)))
        {
            _fruitless = 0;
        }
        else
        {
            _fruitless++;
        }
        _lookedFor = new HashSet<string>(Missing(now), StringComparer.OrdinalIgnoreCase);
        _lastSearch = now;
    }

    // How long to wait after the last search for the same machines.
    internal TimeSpan Wait()
    {
        var wait = Interval;
        for (var i = 0; i < _fruitless && wait < LongestInterval; i++)
        {
            wait += wait;
        }
        return wait < LongestInterval ? wait : LongestInterval;
    }

    private List<string> Missing(DateTime now) =>
        _offlineSince.Where(pair => now - pair.Value >= OfflineFor).Select(pair => pair.Key).ToList();
}
