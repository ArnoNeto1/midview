using Midview.Master;
using Xunit;

public class ScreenLockTextTests
{
    [Fact]
    public void ALockedMachine_SaysSo() =>
        Assert.Equal(("🔒 Tela bloqueada", false), ScreenLockText.Badge(locked: true, wasLocked: false, "tela bloqueada"));

    // Signed out, restarted or switched user: the teacher should know who
    // got out, until the machine is locked again.
    [Fact]
    public void AMachineThatWasLockedAndIsntAnymore_GotOutOfTheLock() =>
        Assert.Equal(("⚠ Saiu do bloqueio (Sem usuário logado)", true),
            ScreenLockText.Badge(locked: false, wasLocked: true, "Sem usuário logado"));

    [Fact]
    public void AMachineThatWasNeverLocked_WasntLocked() =>
        Assert.Equal(("⚠ Não bloqueou (Offline)", true), ScreenLockText.Badge(locked: false, wasLocked: false, "Offline"));

    [Fact]
    public void BackInTheLock_IsLockedAgain() =>
        Assert.Equal(("🔒 Tela bloqueada", false), ScreenLockText.Badge(locked: true, wasLocked: true, "tela bloqueada"));

    // One toolbar button, for the machines it reaches (ScreenLockReach): it
    // locks them, and once every one is locked it becomes the one that gives
    // them back. Its padlock shows how those screens are (open while any is
    // free, closed while all are locked), and its label keeps one word per
    // line either way, so the button doesn't change shape when it flips.
    [Fact]
    public void TheClassButton_LocksWhileNothingIsLocked()
    {
        var button = ScreenLockText.ClassButton(releases: false);

        Assert.Equal("Bloquear\ntelas", button.Label);
        Assert.Equal("\uE785", button.Badge); // open padlock
    }

    [Fact]
    public void TheClassButton_UnlocksOnceEverythingItReachesIsLocked()
    {
        var button = ScreenLockText.ClassButton(releases: true);

        Assert.Equal("Desbloquear\ntelas", button.Label);
        Assert.Equal("\uE72E", button.Badge); // closed padlock
        Assert.Contains("os selecionados, ou todos os que a grade mostra", button.ToolTip);
    }

    // One item in a machine's menu too, by the same rule for the machines it acts on.
    [Fact]
    public void TheMachineMenu_LocksWhileNoneOfItsMachinesIsLocked() =>
        Assert.Equal("Bloquear a tela", ScreenLockText.MachineMenuItem(anyLocked: false));

    [Fact]
    public void TheMachineMenu_UnlocksOnceAnyOfItsMachinesIsLocked() =>
        Assert.Equal("Desbloquear a tela", ScreenLockText.MachineMenuItem(anyLocked: true));
}
