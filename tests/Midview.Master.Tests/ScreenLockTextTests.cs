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

    // One toolbar button: it locks the class, and once any screen is
    // locked it becomes the one that gives them all back.
    [Fact]
    public void TheClassButton_LocksWhileNothingIsLocked()
    {
        var button = ScreenLockText.ClassButton(anyLocked: false);

        Assert.Equal("Bloquear telas", button.Label);
        Assert.Equal("", button.Badge);
    }

    [Fact]
    public void TheClassButton_UnlocksOnceAnythingIsLocked()
    {
        var button = ScreenLockText.ClassButton(anyLocked: true);

        Assert.Equal("Desbloquear telas", button.Label);
        Assert.Equal("", button.Badge);
        Assert.Contains("todos os alunos", button.ToolTip);
    }
}
