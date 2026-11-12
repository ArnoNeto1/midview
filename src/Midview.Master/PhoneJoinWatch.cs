namespace Midview.Master;

// The phones just prepared over the cable that the Master is waiting for:
// each is saved without an address, and only a search of the network finds
// it. While any is waited for, one search every 10 s serves them all; a phone
// stops being waited for once it answers, or 5 minutes after it was added
// (or added again, "Procurar de novo"). Keyed by the phone's agent thumbprint.
internal sealed class PhoneJoinWatch
{
    public static readonly TimeSpan SearchEvery = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, DateTimeOffset> _since = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastSearch;

    public bool IsEmpty => _since.Count == 0;

    public bool IsWatching(string thumbprint) => _since.ContainsKey(thumbprint);

    // From now on for 5 minutes; the next Tick asks for a search at once.
    public void Watch(string thumbprint, DateTimeOffset now)
    {
        _since[thumbprint] = now;
        _lastSearch = null;
    }

    // Drops the phones that answer or whose 5 minutes are over; true when a
    // search is due for the ones left.
    public bool Tick(DateTimeOffset now, Func<string, bool> isOnline)
    {
        foreach (var (thumbprint, since) in _since.ToList())
        {
            if (isOnline(thumbprint) || now - since >= GiveUpAfter)
            {
                _since.Remove(thumbprint);
            }
        }
        return !IsEmpty && (_lastSearch is not { } last || now - last >= SearchEvery);
    }

    public void Searched(DateTimeOffset now) => _lastSearch = now;
}
