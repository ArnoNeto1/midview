using System.Net;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Whether a colleague's teacher PC may work this PC's "Pasta da turma" window
// (ClassFolderHost; plan decision 5, Rulings 14 and 15): the room's online
// Agents are asked at once whether they trust that teacher (ClassFolderClient.
// IsTeacherTrusted), and the first yes admits it. A yes is kept 10 minutes and
// a no 30 seconds, so a colleague's PC asking every couple of seconds doesn't
// ask the room each time, and one just given access ("Dar acesso a um colega")
// gets in within half a minute; a no never replaces a yes still in date. At
// most maxKept answers are kept, the oldest no going first.
//
// Asking the room is a round. One teacher has one round at a time, which every
// check for it shares; and one address (where the request came from) starts
// one round at a time, so a stranger making a new certificate for every
// connection can't set the room asking about all of them at once: its other
// checks wait for that round, then go on. A whole check takes at most wait (5 s
// by default, well inside the colleague's own wait for an answer), waiting
// included: when it runs out, or no Agent gave an answer at all (none online,
// none reachable, none that knows the question), the answer is no, and it isn't
// kept.
//
// masterCertificate: this PC's, which the Agents trust. machines and isOnline:
// the classroom list and the grid's view of each machine (called from
// background threads; a machine whose view can't be had counts as off).
// utcNow: the clock the answers are kept by.
public sealed class ColleagueTrust
{
    public static readonly TimeSpan TrustedFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RefusedFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);
    public const int DefaultMaxKept = 256;

    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly Func<DateTime> _utcNow;
    private readonly int _agentPort;
    private readonly TimeSpan _wait;
    private readonly int _maxKept;

    // All under _lock: the answers kept, by thumbprint (upper case), and the
    // rounds out, by the thumbprint they are about and the address that started them.
    private readonly object _lock = new();
    private readonly Dictionary<string, Kept> _answers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<bool?>> _roundsFor = new(StringComparer.Ordinal);
    private readonly Dictionary<IPAddress, Task<bool?>> _roundsFrom = new();
    private long _keptCount;

    public ColleagueTrust(
        X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline,
        Func<DateTime>? utcNow = null, int agentPort = ConnectionClient.AgentPort, TimeSpan? wait = null,
        int maxKept = DefaultMaxKept)
    {
        if (wait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wait));
        }
        if (maxKept < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxKept));
        }
        _masterCertificate = masterCertificate;
        _machines = machines;
        _isOnline = isOnline;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _agentPort = agentPort;
        _wait = wait ?? DefaultWait;
        _maxKept = maxKept;
    }

    // For the tests: how many rounds of the room are out now.
    internal int RoundsOut
    {
        get
        {
            lock (_lock)
            {
                return _roundsFor.Count;
            }
        }
    }

    // from: the address the request came from. Never throws: anything that
    // goes wrong asking is no answer from that Agent.
    public async Task<bool> IsTrustedAsync(string thumbprint, IPAddress from)
    {
        if (!TeacherInvite.IsThumbprint(thumbprint))
        {
            return false;
        }
        var key = thumbprint.ToUpperInvariant();
        Task? deadline = null;
        while (true)
        {
            Task<bool?> round;
            bool aboutThisOne;
            lock (_lock)
            {
                if (_answers.TryGetValue(key, out var kept) && _utcNow() < kept.Until)
                {
                    return kept.Trusted;
                }
                if (_roundsFor.TryGetValue(key, out var same))
                {
                    (round, aboutThisOne) = (same, true);
                }
                else if (_roundsFrom.TryGetValue(from, out var other))
                {
                    (round, aboutThisOne) = (other, false); // that address's round first
                }
                else
                {
                    (round, aboutThisOne) = (StartRound(key, from), true);
                }
            }

            deadline ??= Task.Delay(_wait);
            if (await Task.WhenAny(round, deadline).ConfigureAwait(false) == deadline)
            {
                return false;
            }
            if (aboutThisOne)
            {
                return round.Result == true;
            }
        }
    }

    // Under _lock. The round runs off this thread, so it is known as out before it can end.
    private Task<bool?> StartRound(string key, IPAddress from)
    {
        var round = Task.Run(() => RoundAsync(key, from));
        _roundsFor[key] = round;
        _roundsFrom[from] = round;
        return round;
    }

    private async Task<bool?> RoundAsync(string key, IPAddress from)
    {
        bool? answer = null;
        try
        {
            answer = await AskTheRoomAsync(key).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                Keep(key, answer);
                _roundsFor.Remove(key);
                _roundsFrom.Remove(from);
            }
        }
        return answer;
    }

    // Under _lock. A no never replaces a yes still in date; answers out of
    // date go, and then, while there are too many, the oldest no, and only
    // then the oldest yes (strangers asking can't push a colleague's yes out).
    private void Keep(string key, bool? answer)
    {
        if (answer is not { } trusted)
        {
            return;
        }
        var now = _utcNow();
        if (!trusted && _answers.TryGetValue(key, out var known) && known.Trusted && now < known.Until)
        {
            return;
        }

        _answers.Remove(key);
        foreach (var stale in _answers.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToList())
        {
            _answers.Remove(stale);
        }
        while (_answers.Count >= _maxKept)
        {
            _answers.Remove(_answers.MinBy(entry => (entry.Value.Trusted, entry.Value.Order)).Key);
        }
        _answers[key] = new Kept(trusted, now + (trusted ? TrustedFor : RefusedFor), ++_keptCount);
    }

    // True at the first yes; false when every Agent asked answered and one said
    // no; null when none answered, or the wait ran out first.
    private async Task<bool?> AskTheRoomAsync(string thumbprint)
    {
        List<Task<bool?>> pending;
        try
        {
            // Each on a thread of its own: an exchange blocks on the network
            // (up to its own timeouts, past this check's), and a room's worth
            // of them must not hold the thread pool.
            pending = _machines()
                .Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine))
                .Select(machine => Task.Factory.StartNew(
                    () => Ask(machine, thumbprint), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
                .ToList();
        }
        catch (Exception)
        {
            return null; // the classroom list couldn't be read: nobody to ask
        }

        var deadline = Task.Delay(_wait);
        var saidNo = false;
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending.Cast<Task>().Append(deadline)).ConfigureAwait(false);
            if (done == deadline)
            {
                return null;
            }
            var answered = (Task<bool?>)done;
            pending.Remove(answered);
            switch (answered.Result)
            {
                case true:
                    return true;
                case false:
                    saidNo = true;
                    break;
            }
        }
        return saidNo ? false : null;
    }

    // A machine whose view can't be had just now (the list changing under it, say) counts as off.
    private bool IsOnline(KnownMachine machine)
    {
        try
        {
            return _isOnline(machine);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // That Agent's answer; null when it couldn't be had.
    private bool? Ask(KnownMachine machine, string thumbprint)
    {
        try
        {
            return ClassFolderClient.IsTeacherTrusted(machine, _masterCertificate, thumbprint, _agentPort);
        }
        catch (Exception)
        {
            return null; // off, unreachable, or answering nonsense
        }
    }

    // An answer kept: until when it holds, and when it was kept (Order counts up).
    private readonly record struct Kept(bool Trusted, DateTime Until, long Order);
}
