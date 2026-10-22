using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Whether a colleague's teacher PC may work this PC's "Pasta da turma" window
// (ClassFolderHost; plan decision 5, Ruling 14): the room's online Agents are
// asked at once whether they trust that teacher (ClassFolderClient.
// IsTeacherTrusted), and the first yes admits it. A yes is kept 10 minutes and
// a no 30 seconds, so a colleague's PC asking every couple of seconds doesn't
// ask the room each time, and one just given access ("Dar acesso a um colega")
// gets in within half a minute. A whole check takes at most wait (5 s by
// default, well inside the colleague's own wait for an answer): when it runs
// out, or no Agent gave an answer at all (none online, none reachable, none
// that knows the question), the answer is no, and it isn't kept.
//
// masterCertificate: this PC's, which the Agents trust. machines and isOnline:
// the classroom list and the grid's view of each machine (called from
// background threads). utcNow: the clock the answers are kept by.
public sealed class ColleagueTrust
{
    public static readonly TimeSpan TrustedFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RefusedFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly Func<DateTime> _utcNow;
    private readonly int _agentPort;
    private readonly TimeSpan _wait;

    // By thumbprint (upper case): the answer, and until when it holds.
    private readonly ConcurrentDictionary<string, (bool Trusted, DateTime Until)> _answers = new(StringComparer.Ordinal);

    public ColleagueTrust(
        X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline,
        Func<DateTime>? utcNow = null, int agentPort = ConnectionClient.AgentPort, TimeSpan? wait = null)
    {
        if (wait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wait));
        }
        _masterCertificate = masterCertificate;
        _machines = machines;
        _isOnline = isOnline;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _agentPort = agentPort;
        _wait = wait ?? DefaultWait;
    }

    // Never throws: anything that goes wrong asking is no answer from that Agent.
    public async Task<bool> IsTrustedAsync(string thumbprint)
    {
        if (!TeacherInvite.IsThumbprint(thumbprint))
        {
            return false;
        }
        var key = thumbprint.ToUpperInvariant();
        if (_answers.TryGetValue(key, out var known) && _utcNow() < known.Until)
        {
            return known.Trusted;
        }

        var answer = await AskTheRoomAsync(key).ConfigureAwait(false);
        if (answer is { } trusted)
        {
            _answers[key] = (trusted, _utcNow() + (trusted ? TrustedFor : RefusedFor));
        }
        return answer == true;
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
                .Where(machine => machine.AgentThumbprint is { Length: > 0 } && _isOnline(machine))
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
}
