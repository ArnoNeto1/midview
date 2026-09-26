using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

// The lab's power timetable on this machine (MessageType.SetPowerSchedule):
// kept on disk, and followed by shutting the machine down on its own at the
// scheduled time, with the teacher's PC on or off. Only the shutdown is this
// machine's to do; waking it is the teacher's PC's job.
public sealed class ScheduledShutdown
{
    // A machine that was asleep through the scheduled time and wakes up
    // shortly after still shuts down; much later (someone turned it on for
    // an evening class) it doesn't.
    public static readonly TimeSpan MaxLateness = TimeSpan.FromMinutes(30);

    private readonly string _statePath;
    private readonly Func<string[], int> _runShutdown;
    private readonly object _gate = new();
    private PowerSchedule? _schedule;

    public ScheduledShutdown(string statePath, Func<string[], int> runShutdown)
    {
        _statePath = statePath;
        _runShutdown = runShutdown;
        _schedule = Load(statePath);
    }

    public static string DefaultStatePath => Path.Combine(AgentDataDirectory.DirectoryPath, "power-schedule.json");

    public PowerSchedule? Current
    {
        get
        {
            lock (_gate)
            {
                return _schedule;
            }
        }
    }

    // The newest schedule wins (see PowerSchedule.SavedAt): an older one,
    // from a Master that hasn't seen the latest change yet, is kept out.
    public (bool Succeeded, string Detail) Handle(byte[] payload)
    {
        if (!PowerSchedule.TryDecode(payload, out var schedule))
        {
            return (false, "pedido inválido");
        }

        lock (_gate)
        {
            if (_schedule is not null && schedule.SavedAt < _schedule.SavedAt)
            {
                var savedAt = DateTimeOffset.FromUnixTimeSeconds(_schedule.SavedAt).ToLocalTime();
                return (true, string.Create(CultureInfo.InvariantCulture,
                    $"mantido o agendamento mais novo, salvo em {savedAt:dd/MM HH:mm}: {Describe(_schedule)}"));
            }

            try
            {
                Save(_statePath, schedule);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLog.Warn($"Couldn't save the power schedule: {ex.Message}");
                return (false, $"não foi possível gravar o agendamento: {ex.Message}");
            }

            _schedule = schedule;
            return (true, Describe(schedule));
        }
    }

    // Called every few seconds with the time of the previous call: shuts
    // down when the scheduled time fell between the two. A machine turned on
    // after the time never shuts down straight away.
    public bool Check(DateTime previousCheck, DateTime now)
    {
        var schedule = Current;
        if (schedule is null || !IsDue(schedule, previousCheck, now))
        {
            return false;
        }

        try
        {
            var exitCode = _runShutdown(ShutdownCommands.BuildScheduled(PowerSchedule.ShutdownWarningSeconds));
            if (exitCode != 0)
            {
                // 1190: a shutdown is already under way, e.g. the teacher's.
                AgentLog.Warn($"The scheduled shutdown's shutdown.exe exited with code {exitCode}.");
            }
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Running shutdown.exe for the scheduled shutdown failed: {ex.Message}");
        }
        return true;
    }

    public static bool IsDue(PowerSchedule schedule, DateTime previousCheck, DateTime now)
    {
        if (schedule.ShutdownMinute is not int minute || now <= previousCheck)
        {
            return false;
        }

        // Yesterday's too, for a check that straddles midnight.
        foreach (var date in new[] { now.Date, now.Date.AddDays(-1) })
        {
            var due = date.AddMinutes(minute);
            if (schedule.IsScheduledOn(date.DayOfWeek) && previousCheck < due && due <= now && now - due <= MaxLateness)
            {
                return true;
            }
        }
        return false;
    }

    public static string Describe(PowerSchedule schedule) =>
        schedule.ShutdownMinute is int minute && schedule.Days != 0
            ? $"desliga sozinha às {PowerSchedule.FormatTime(minute)} ({PowerSchedule.DescribeDays(schedule.Days)})"
            : "sem desligamento programado";

    // Unreadable means no schedule: a machine that doesn't shut down on its
    // own is the safe side. The file is kept aside, as with SiteBlockState.
    private static PowerSchedule? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var schedule = JsonSerializer.Deserialize<PowerSchedule>(File.ReadAllText(path));
            return schedule is not null && schedule.IsValid() ? schedule : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't read the power schedule, following none: {ex.Message}");
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Left in place; it's read again (and fails again) next start.
            }
            return null;
        }
    }

    private static void Save(string path, PowerSchedule schedule)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, schedule);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}

// Follows ScheduledShutdown: checks the clock every few seconds.
public sealed class ScheduledShutdownWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);

    private readonly ScheduledShutdown _scheduledShutdown;

    public ScheduledShutdownWorker(ScheduledShutdown scheduledShutdown)
    {
        _scheduledShutdown = scheduledShutdown;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var previousCheck = DateTime.Now;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTime.Now;
            _scheduledShutdown.Check(previousCheck, now);
            previousCheck = now;
        }
    }
}
