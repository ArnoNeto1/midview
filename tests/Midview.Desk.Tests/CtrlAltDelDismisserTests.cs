using Midview.Desk;
using Xunit;

// While the screen is locked, the Ctrl+Alt+Del screen a student opens is closed
// with Esc. Every source is scripted here, so no desktop, process list or key
// press is real.
public sealed class CtrlAltDelDismisserTests
{
    private static readonly DateTime Start = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    private bool _locked = true;
    private string? _desktop = "Winlogon";
    private bool _loggedOn = true;
    private bool _uac;
    private int _presses;
    private readonly List<string> _log = new();

    private CtrlAltDelDismisser Dismisser() =>
        new(() => _locked, () => _desktop, () => _loggedOn, () => _uac, () => _presses++, _log.Add);

    [Fact]
    public void TheCtrlAltDelScreen_IsClosedOnceItHadTimeToComeUp()
    {
        var dismisser = Dismisser();

        dismisser.Poll(Start);
        Assert.Equal(0, _presses);

        dismisser.Poll(Start + CtrlAltDelDismisser.FirstPressAfter);
        Assert.Equal(1, _presses);
    }

    // An Esc that came too early (or was lost) is tried again, but not on every poll.
    [Fact]
    public void AScreenStillUp_IsPressedAgainOnlyAfterAPause()
    {
        var dismisser = Dismisser();
        var first = Start + CtrlAltDelDismisser.FirstPressAfter;
        dismisser.Poll(Start);
        dismisser.Poll(first);

        dismisser.Poll(first + TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, _presses);

        dismisser.Poll(first + CtrlAltDelDismisser.PressAgainAfter);
        Assert.Equal(2, _presses);
    }

    [Fact]
    public void NotLocked_TheStudentKeepsTheirCtrlAltDel()
    {
        _locked = false;
        var dismisser = Dismisser();

        dismisser.Poll(Start);
        dismisser.Poll(Start + TimeSpan.FromSeconds(5));

        Assert.Equal(0, _presses);
    }

    // The UAC prompt is on the same secure desktop: Esc there would refuse it,
    // even one the teacher is answering by remote control.
    [Fact]
    public void AUacPrompt_IsLeftAlone()
    {
        _uac = true;
        var dismisser = Dismisser();

        dismisser.Poll(Start);
        dismisser.Poll(Start + TimeSpan.FromSeconds(5));

        Assert.Equal(0, _presses);
    }

    // Nobody logged on: that is the sign-in screen, and Esc there would keep a
    // student from logging on while the lock is on.
    [Fact]
    public void TheSignInScreen_IsLeftAlone()
    {
        _loggedOn = false;
        var dismisser = Dismisser();

        dismisser.Poll(Start);
        dismisser.Poll(Start + TimeSpan.FromSeconds(5));

        Assert.Equal(0, _presses);
    }

    [Fact]
    public void TheStudentsOwnDesktop_IsLeftAlone()
    {
        _desktop = "Default";
        var dismisser = Dismisser();

        dismisser.Poll(Start);
        dismisser.Poll(Start + TimeSpan.FromSeconds(5));

        Assert.Equal(0, _presses);
    }

    // Each time the screen comes back it gets its time to come up again.
    [Fact]
    public void TheScreenOpenedAgain_WaitsItsTurnAgain()
    {
        var dismisser = Dismisser();
        dismisser.Poll(Start);
        dismisser.Poll(Start + CtrlAltDelDismisser.FirstPressAfter);
        _desktop = "Default";
        dismisser.Poll(Start + TimeSpan.FromSeconds(1));

        _desktop = "Winlogon";
        var reopened = Start + TimeSpan.FromSeconds(2);
        dismisser.Poll(reopened);
        Assert.Equal(1, _presses);

        dismisser.Poll(reopened + CtrlAltDelDismisser.FirstPressAfter);
        Assert.Equal(2, _presses);
    }

    // It runs on a loop of its own in the helper: a source that fails is logged
    // (once, not on every poll) and nothing is pressed.
    [Fact]
    public void ASourceThatFails_NeverThrows()
    {
        var dismisser = new CtrlAltDelDismisser(
            () => true, () => throw new InvalidOperationException("sem desktop"), () => true, () => false,
            () => _presses++, _log.Add);

        dismisser.Poll(Start);
        dismisser.Poll(Start + TimeSpan.FromSeconds(5));

        Assert.Equal(0, _presses);
        Assert.Contains("sem desktop", Assert.Single(_log));
    }

    [Fact]
    public void TheLockSignal_IsReadFromItsNamedEvent()
    {
        var name = $@"Local\MidviewTest-{Guid.NewGuid():N}";
        Assert.False(CtrlAltDelScreen.ScreenLocked(name)); // no Agent, no lock

        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        Assert.False(CtrlAltDelScreen.ScreenLocked(name));

        signal.Set();
        Assert.True(CtrlAltDelScreen.ScreenLocked(name));
    }
}
