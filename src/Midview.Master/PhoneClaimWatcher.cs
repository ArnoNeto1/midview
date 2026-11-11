using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// While QRs are waiting for their phones, looks for them on the network every
// few seconds: a phone advertising the claim id of a pending token is claimed
// and joins the class by itself.
public sealed class PhoneClaimWatcher : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly Func<IReadOnlyList<FoundMachine>> _search;
    private readonly Func<FoundMachine, PendingPhoneClaim, KnownMachine> _claim;
    private readonly Action<KnownMachine> _added;
    private readonly Func<DateTimeOffset> _now;
    private readonly string? _folder;
    private readonly object _round = new();
    private readonly Timer? _timer;
    private volatile bool _disposed;
    private volatile bool _again;

    // timed: false for tests, which call CheckOnce themselves.
    public PhoneClaimWatcher(
        Func<IReadOnlyList<FoundMachine>> search, Func<FoundMachine, PendingPhoneClaim, KnownMachine> claim,
        Action<KnownMachine> added, Func<DateTimeOffset> now, string? folder = null, bool timed = true)
    {
        _search = search;
        _claim = claim;
        _added = added;
        _now = now;
        _folder = folder;
        if (timed)
        {
            _timer = new Timer(_ => Tick(), null, Interval, Interval);
        }
    }

    // A QR was just shown: look now instead of waiting for the next round.
    // A Kick during a round is not lost: Tick finds the round busy and
    // asks for another one. After Dispose it does nothing.
    public void Kick()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            _timer?.Change(TimeSpan.Zero, Interval);
        }
        catch (ObjectDisposedException)
        {
            // Disposed between the check and the call.
        }
    }

    // One round. False when nothing was pending (nothing was searched).
    public bool CheckOnce()
    {
        lock (_round)
        {
            var pending = PendingPhoneClaimsFile.LoadValid(_now(), _folder).ToList();
            if (pending.Count == 0 || _disposed)
            {
                return false;
            }

            var seen = _search();
            foreach (var found in seen)
            {
                if (_disposed)
                {
                    break;
                }
                if (found.ClaimId is null)
                {
                    continue;
                }
                var claim = pending.FirstOrDefault(p => string.Equals(PhoneClaim.ClaimId(p.Token), found.ClaimId, StringComparison.OrdinalIgnoreCase));
                if (claim is null)
                {
                    continue;
                }

                KnownMachine machine;
                try
                {
                    machine = _claim(found, claim);
                }
                catch (Exception)
                {
                    // Stays pending: the next round tries again.
                    continue;
                }
                // One proof works once: no other device in this round gets it.
                pending.Remove(claim);
                PendingPhoneClaimsFile.Remove(claim.Token, _folder);
                _added(machine);
            }
            return true;
        }
    }

    // A round still running (a phone stalling the TLS step) means this tick
    // is skipped, not queued behind it; the running round goes again instead.
    private void Tick()
    {
        if (_disposed)
        {
            return;
        }
        if (!Monitor.TryEnter(_round))
        {
            _again = true;
            return;
        }
        try
        {
            do
            {
                _again = false;
                CheckOnce();
            }
            while (_again && !_disposed);
        }
        catch (Exception)
        {
            // A bad round (network down, disk) must not kill the timer.
        }
        finally
        {
            Monitor.Exit(_round);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }
}

// The roster after a prepared phone joined: a phone already listed with the
// same name (the same phone coming back after a wipe) is replaced; a PC with
// that name is left alone.
public static class PhoneJoinRoster
{
    public static IReadOnlyList<KnownMachine> ReplacingSameName(IReadOnlyList<KnownMachine> roster, KnownMachine joined) =>
        roster.Where(m => !(m.IsPhone && string.Equals(m.Name, joined.Name, StringComparison.OrdinalIgnoreCase)))
            .Append(joined).ToList();

    // Runs the step that adds the joined phone and saves the roster. By then
    // the claim is gone and the phone trusts this PC, so a save that fails is
    // not a reason to hide the phone: null when it all worked, else the text
    // for the status bar.
    public static string? AddAndSave(Action addAndSave)
    {
        try
        {
            addAndSave();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return "O celular entrou na turma, mas a lista de máquinas não pôde ser guardada: " + ErrorText.Describe(ex);
        }
    }
}
