using Midview.Agent;
using Midview.Protocol;
using Xunit;

// "Instalar as atualizações do Windows que estiverem esperando": Windows'
// own "Atualizar e desligar", asked for through InitiateShutdown instead of
// shutdown.exe (which has no switch for it).
public class PowerCommandHandlerUpdatesTests
{
    private readonly List<string[]> _shutdownExeRuns = new();
    private readonly List<UpdatingShutdown> _updatingRuns = new();

    private PowerCommandHandler Handler(int updatingResult = 0, ICountdownDisplay? countdown = null, Func<DateTime>? utcNow = null) =>
        new(args =>
            {
                _shutdownExeRuns.Add(args);
                return 0;
            },
            utcNow,
            countdown,
            request =>
            {
                _updatingRuns.Add(request);
                return updatingResult;
            });

    [Fact]
    public void ARestartInstallingUpdates_WarnsTheStudentOfBoth()
    {
        var request = ShutdownCommands.BuildWithUpdates(PowerAction.Restart, 60);

        Assert.True(request.Restart);
        Assert.Equal(60, request.DelaySeconds);
        Assert.Equal(
            "O professor vai instalar as atualizações do Windows e reiniciar este computador em 1 minuto. " +
            "Salve seu trabalho agora: o que não estiver salvo será perdido.",
            request.Warning);
    }

    [Fact]
    public void AShutdownInstallingUpdatesNow_HasNoWarningToShow()
    {
        Assert.Equal(new UpdatingShutdown(false, 0, null), ShutdownCommands.BuildWithUpdates(PowerAction.Shutdown, 0));
    }

    // Like shutdown.exe's /f, then power off or restart, installing what waits.
    [Fact]
    public void TheFlags_ForceTheSessionsClosed_AndInstallTheUpdates()
    {
        Assert.Equal(0x1u | 0x2u | 0x40u | 0x8u, WindowsUpdatingShutdown.Flags(restart: false));
        Assert.Equal(0x1u | 0x2u | 0x40u | 0x4u, WindowsUpdatingShutdown.Flags(restart: true));
    }

    [Theory]
    [InlineData(PowerAction.Shutdown, 300, 0, true, "desliga em 5 minutos, instalando as atualizações")]
    [InlineData(PowerAction.Restart, 60, 0, true, "reinicia em 1 minuto, instalando as atualizações")]
    [InlineData(PowerAction.Shutdown, 0, 0, true, "desligando agora, instalando as atualizações")]
    [InlineData(PowerAction.Restart, 0, 0, true, "reiniciando agora, instalando as atualizações")]
    [InlineData(PowerAction.Shutdown, 60, 1190, false, "já há um desligamento agendado (cancele antes de pedir outro)")]
    [InlineData(PowerAction.Restart, 60, 5, false, "o Windows recusou (código 5)")]
    public void Handle_InstallingUpdates_GoesThroughWindowsAndSaysWhatHappened(
        PowerAction action, int delaySeconds, int result, bool succeeded, string detail)
    {
        Assert.Equal((succeeded, detail), Handler(result).Handle(PowerCommand.Encode(action, delaySeconds, installUpdates: true)));

        Assert.Equal(new[] { ShutdownCommands.BuildWithUpdates(action, delaySeconds) }, _updatingRuns);
        Assert.Empty(_shutdownExeRuns);
    }

    // The request every Agent knew still goes through shutdown.exe.
    [Fact]
    public void Handle_WithoutUpdates_StillRunsShutdownExe()
    {
        Handler().Handle(PowerCommand.Encode(PowerAction.Restart, 60));

        Assert.Single(_shutdownExeRuns);
        Assert.Empty(_updatingRuns);
    }

    [Fact]
    public void Handle_InstallingUpdates_CountsDownAndIsPending_LikeAnyShutdown()
    {
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var countdown = new RecordingCountdownDisplay();
        var handler = Handler(countdown: countdown, utcNow: () => now);

        handler.Handle(PowerCommand.Encode(PowerAction.Restart, 60, installUpdates: true));

        Assert.Equal(new[] { (PowerCountdown.Restart, 60) }, countdown.Shown);
        Assert.True(handler.IsShutdownPending());

        // "Cancelar reinício" calls it off through shutdown.exe /a, as any other.
        handler.Handle(PowerCommand.Encode(PowerAction.Cancel, 0));
        Assert.Equal(1, countdown.Closed);
        Assert.False(handler.IsShutdownPending());
    }

    [Fact]
    public void Handle_InstallingUpdates_WhenWindowsThrows_IsReported()
    {
        var handler = new PowerCommandHandler(_ => 0, runUpdatingShutdown: _ => throw new System.ComponentModel.Win32Exception("acesso negado"));

        var (succeeded, detail) = handler.Handle(PowerCommand.Encode(PowerAction.Shutdown, 60, installUpdates: true));

        Assert.False(succeeded);
        Assert.Contains("acesso negado", detail);
    }

    // A handler built without the Windows call refuses rather than shut down
    // without the updates the teacher asked for.
    [Fact]
    public void Handle_InstallingUpdates_WithoutTheWindowsCall_IsRefused()
    {
        var handler = new PowerCommandHandler(args =>
        {
            _shutdownExeRuns.Add(args);
            return 0;
        });

        var (succeeded, _) = handler.Handle(PowerCommand.Encode(PowerAction.Shutdown, 60, installUpdates: true));

        Assert.False(succeeded);
        Assert.Empty(_shutdownExeRuns);
    }
}
