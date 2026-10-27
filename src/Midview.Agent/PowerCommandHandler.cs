using Midview.Protocol;

namespace Midview.Agent;

// The shutdown.exe arguments for each power command. With a delay, Windows
// shows the logged-in student the comment below (the countdown the student
// actually watches is Midview's banner, see ICountdownDisplay); when it
// ends, open programs are closed without saving (/f) -- otherwise one
// unsaved document would keep an unattended lab machine on all night.
public static class ShutdownCommands
{
    public static string[] Build(PowerAction action, int delaySeconds) => action switch
    {
        PowerAction.Cancel => new[] { "/a" },
        _ => new[] { action == PowerAction.Restart ? "/r" : "/s", "/f", "/t", delaySeconds.ToString(), "/d", "p:0:0" }
            .Concat(delaySeconds > 0 ? new[] { "/c", Warning(action, delaySeconds) } : Array.Empty<string>())
            .ToArray(),
    };

    // The lab's timetable (ScheduledShutdown): the same forced shutdown, with
    // a warning that says it's the schedule rather than the teacher now.
    public static string[] BuildScheduled(int delaySeconds) =>
        new[]
        {
            "/s", "/f", "/t", delaySeconds.ToString(), "/d", "p:0:0", "/c",
            $"Desligamento programado: este computador vai desligar em {DescribeDelay(delaySeconds)}. " +
            "Salve seu trabalho agora: o que não estiver salvo será perdido.",
        };

    // Installing the Windows updates waiting for a restart on the way
    // (WindowsUpdatingShutdown), with a warning that says so.
    public static UpdatingShutdown BuildWithUpdates(PowerAction action, int delaySeconds)
    {
        var restart = action == PowerAction.Restart;
        return new UpdatingShutdown(restart, delaySeconds, delaySeconds > 0
            ? $"O professor vai instalar as atualizações do Windows e {(restart ? "reiniciar" : "desligar")} este computador em " +
              $"{DescribeDelay(delaySeconds)}. Salve seu trabalho agora: o que não estiver salvo será perdido."
            : null);
    }

    public static string DescribeDelay(int delaySeconds) =>
        delaySeconds % 60 == 0
            ? delaySeconds == 60 ? "1 minuto" : $"{delaySeconds / 60} minutos"
            : $"{delaySeconds} segundos";

    private static string Warning(PowerAction action, int delaySeconds) =>
        $"O professor vai {(action == PowerAction.Restart ? "reiniciar" : "desligar")} este computador em " +
        $"{DescribeDelay(delaySeconds)}. Salve seu trabalho agora: o que não estiver salvo será perdido.";
}

// Runs the teacher's shutdown, restart or cancel through shutdown.exe and
// says what happened; one that installs the waiting Windows updates goes
// through Windows' own call for it instead (runUpdatingShutdown). The Agent
// runs as SYSTEM, so this works whether or not anyone is logged in.
public sealed class PowerCommandHandler
{
    // shutdown.exe exit codes and InitiateShutdown results (Windows system error codes).
    private const int NoShutdownInProgress = 1116; // ERROR_NO_SHUTDOWN_IN_PROGRESS
    private const int ShutdownAlreadyScheduled = 1190; // ERROR_SHUTDOWN_IS_SCHEDULED
    private const int ShutdownInProgress = 1115; // ERROR_SHUTDOWN_IN_PROGRESS
    // InitiateShutdown's answer with fast user switching on and nobody logged on.
    private const int NotReady = 21; // ERROR_NOT_READY

    // A delayed shutdown keeps counting down in Windows after the Agent
    // stops, so a network update started now would be cut short by it.
    private static readonly TimeSpan PendingMargin = TimeSpan.FromMinutes(2);

    // Installing the waiting updates happens before Windows stops the
    // services, so this Agent keeps answering through it, which can take
    // half an hour and more on a slow machine.
    private static readonly TimeSpan PendingMarginInstallingUpdates = TimeSpan.FromMinutes(60);

    private readonly Func<string[], int> _runShutdown;
    private readonly Func<UpdatingShutdown, int>? _runUpdatingShutdown;
    private readonly Func<DateTime> _utcNow;
    private readonly ICountdownDisplay? _countdown;
    private readonly object _gate = new();
    private DateTime? _pendingUntil;

    // runUpdatingShutdown: WindowsUpdatingShutdown.Initiate; without it a
    // request to install the updates is refused, never run without them.
    public PowerCommandHandler(
        Func<string[], int> runShutdown, Func<DateTime>? utcNow = null, ICountdownDisplay? countdown = null,
        Func<UpdatingShutdown, int>? runUpdatingShutdown = null)
    {
        _runShutdown = runShutdown;
        _runUpdatingShutdown = runUpdatingShutdown;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _countdown = countdown;
    }

    // A shutdown or restart this Agent started is still counting down.
    public bool IsShutdownPending()
    {
        lock (_gate)
        {
            return _pendingUntil is { } until && _utcNow() < until;
        }
    }

    public (bool Succeeded, string Detail) Handle(byte[] payload)
    {
        if (!PowerCommand.TryDecode(payload, out var action, out var delaySeconds, out var installUpdates))
        {
            return (false, "pedido inválido");
        }
        if (installUpdates && _runUpdatingShutdown is null)
        {
            return (false, "este Midview não instala atualizações ao desligar");
        }

        int exitCode;
        try
        {
            exitCode = installUpdates
                ? _runUpdatingShutdown!(ShutdownCommands.BuildWithUpdates(action, delaySeconds))
                : _runShutdown(ShutdownCommands.Build(action, delaySeconds));
        }
        catch (Exception ex) when (installUpdates)
        {
            AgentLog.Warn($"Asking Windows to shut down installing updates failed: {ex.Message}");
            return (false, $"o Windows não aceitou o pedido: {ErrorText.Describe(ex)}");
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Running shutdown.exe failed: {ex.Message}");
            return (false, $"não foi possível executar o shutdown.exe: {ErrorText.Describe(ex)}");
        }

        lock (_gate)
        {
            if (action == PowerAction.Cancel && exitCode is 0 or NoShutdownInProgress)
            {
                _pendingUntil = null;
                _countdown?.Close();
            }
            else if (action != PowerAction.Cancel && exitCode == 0)
            {
                _pendingUntil = _utcNow() + TimeSpan.FromSeconds(delaySeconds)
                    + (installUpdates ? PendingMarginInstallingUpdates : PendingMargin);
                if (delaySeconds > 0)
                {
                    _countdown?.Show(action == PowerAction.Restart ? PowerCountdown.Restart : PowerCountdown.Shutdown, delaySeconds);
                }
            }
        }

        var verb = action == PowerAction.Restart ? "reinicia" : "desliga";
        var updates = installUpdates ? ", instalando as atualizações" : string.Empty;
        return (action, exitCode) switch
        {
            (PowerAction.Cancel, 0) => (true, "desligamento cancelado"),
            (PowerAction.Cancel, NoShutdownInProgress) => (true, "nenhum desligamento estava agendado"),
            (_, 0) when delaySeconds == 0 => (true, (action == PowerAction.Restart ? "reiniciando agora" : "desligando agora") + updates),
            (_, 0) => (true, $"{verb} em {ShutdownCommands.DescribeDelay(delaySeconds)}{updates}"),
            (_, ShutdownAlreadyScheduled) => (false, "já há um desligamento agendado (cancele antes de pedir outro)"),
            (_, ShutdownInProgress) => (false, "este computador já está desligando"),
            (_, NotReady) when installUpdates =>
                (false, "o Windows não aceitou instalar as atualizações agora (código 21); tente sem instalar as atualizações"),
            _ => (false, $"o Windows recusou (código {exitCode})"),
        };
    }

    public static int RunShutdownExe(string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("shutdown.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
