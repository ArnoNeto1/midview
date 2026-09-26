using Midview.Protocol;

namespace Midview.Agent;

// The shutdown.exe arguments for each power command. With a delay, Windows
// shows the logged-in student a countdown with the comment below; when it
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

    public static string DescribeDelay(int delaySeconds) =>
        delaySeconds % 60 == 0
            ? delaySeconds == 60 ? "1 minuto" : $"{delaySeconds / 60} minutos"
            : $"{delaySeconds} segundos";

    private static string Warning(PowerAction action, int delaySeconds) =>
        $"O professor vai {(action == PowerAction.Restart ? "reiniciar" : "desligar")} este computador em " +
        $"{DescribeDelay(delaySeconds)}. Salve seu trabalho agora: o que não estiver salvo será perdido.";
}

// Runs the teacher's shutdown, restart or cancel through shutdown.exe and
// says what happened. The Agent runs as SYSTEM, so this works whether or not
// anyone is logged in.
public sealed class PowerCommandHandler
{
    // shutdown.exe exit codes (Windows system error codes).
    private const int NoShutdownInProgress = 1116; // ERROR_NO_SHUTDOWN_IN_PROGRESS
    private const int ShutdownAlreadyScheduled = 1190; // ERROR_SHUTDOWN_IS_SCHEDULED

    // A delayed shutdown keeps counting down in Windows after the Agent
    // stops, so a network update started now would be cut short by it.
    private static readonly TimeSpan PendingMargin = TimeSpan.FromMinutes(2);

    private readonly Func<string[], int> _runShutdown;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private DateTime? _pendingUntil;

    public PowerCommandHandler(Func<string[], int> runShutdown, Func<DateTime>? utcNow = null)
    {
        _runShutdown = runShutdown;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
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
        if (!PowerCommand.TryDecode(payload, out var action, out var delaySeconds))
        {
            return (false, "pedido inválido");
        }

        int exitCode;
        try
        {
            exitCode = _runShutdown(ShutdownCommands.Build(action, delaySeconds));
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Running shutdown.exe failed: {ex.Message}");
            return (false, $"não foi possível executar o shutdown.exe: {ex.Message}");
        }

        lock (_gate)
        {
            if (action == PowerAction.Cancel && exitCode is 0 or NoShutdownInProgress)
            {
                _pendingUntil = null;
            }
            else if (action != PowerAction.Cancel && exitCode == 0)
            {
                _pendingUntil = _utcNow() + TimeSpan.FromSeconds(delaySeconds) + PendingMargin;
            }
        }

        var verb = action == PowerAction.Restart ? "reinicia" : "desliga";
        return (action, exitCode) switch
        {
            (PowerAction.Cancel, 0) => (true, "desligamento cancelado"),
            (PowerAction.Cancel, NoShutdownInProgress) => (true, "nenhum desligamento estava agendado"),
            (_, 0) when delaySeconds == 0 => (true, action == PowerAction.Restart ? "reiniciando agora" : "desligando agora"),
            (_, 0) => (true, $"{verb} em {ShutdownCommands.DescribeDelay(delaySeconds)}"),
            (_, ShutdownAlreadyScheduled) => (false, "já há um desligamento agendado (cancele antes de pedir outro)"),
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
