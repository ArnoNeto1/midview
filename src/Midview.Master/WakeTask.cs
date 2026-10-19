using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Midview.Protocol;

namespace Midview.Master;

// The power-on half of the timetable. A machine that's off can't wake
// itself, so this PC does it: a Windows scheduled task, for the teacher's
// own account (no administrator needed), runs "Midview.Master.exe
// --ligar-turma 07:00" at the wake time on the chosen days. That sends the
// classroom list's magic packets (what the classroom window's "Ligar" sends)
// and exits. Windows wakes this PC from sleep for it, and runs it late if
// the PC was off at the time -- but RunFromCommandLine only wakes the lab up
// to MaxLateness late, so turning the PC on in the afternoon doesn't.
public static class WakeTask
{
    public const string TaskName = "Midview - Ligar a turma";
    public const string CommandLineArgument = "--ligar-turma";
    public static readonly TimeSpan MaxLateness = TimeSpan.FromHours(1);

    // Windows may start a task a moment before its time.
    private static readonly TimeSpan EarlyTolerance = TimeSpan.FromMinutes(5);

    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string LogPath => Path.Combine(MasterData.Folder, "ligar-turma.log");

    // The task's definition, in the XML schtasks.exe imports.
    public static string BuildXml(PowerSchedule schedule, string masterPath, DateTime today)
    {
        var wakeMinute = schedule.WakeMinute ?? throw new ArgumentException("no wake time", nameof(schedule));
        var start = today.Date.AddMinutes(wakeMinute);
        var days = Enum.GetValues<DayOfWeek>().Where(schedule.IsScheduledOn).Select(day => new XElement(Task + day.ToString()));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Task + "Task", new XAttribute("version", "1.2"),
                new XElement(Task + "RegistrationInfo",
                    new XElement(Task + "Description",
                        "Midview: liga as máquinas da sala pela rede (Wake-on-LAN) no horário agendado no MidviewMaster.")),
                new XElement(Task + "Triggers",
                    new XElement(Task + "CalendarTrigger",
                        new XElement(Task + "StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                        new XElement(Task + "Enabled", "true"),
                        new XElement(Task + "ScheduleByWeek",
                            new XElement(Task + "DaysOfWeek", days),
                            new XElement(Task + "WeeksInterval", "1")))),
                new XElement(Task + "Principals",
                    new XElement(Task + "Principal", new XAttribute("id", "Author"),
                        new XElement(Task + "LogonType", "InteractiveToken"),
                        new XElement(Task + "RunLevel", "LeastPrivilege"))),
                new XElement(Task + "Settings",
                    new XElement(Task + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Task + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Task + "StopIfGoingOnBatteries", "false"),
                    new XElement(Task + "StartWhenAvailable", "true"),
                    new XElement(Task + "WakeToRun", "true"),
                    new XElement(Task + "ExecutionTimeLimit", "PT5M"),
                    new XElement(Task + "Enabled", "true")),
                new XElement(Task + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Task + "Exec",
                        new XElement(Task + "Command", masterPath),
                        new XElement(Task + "Arguments", $"{CommandLineArgument} {PowerSchedule.FormatTime(wakeMinute)}")))));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        document.Save(writer);
        return writer.ToString();
    }

    // Creates or replaces the task, or removes it when the schedule has no
    // wake time. runSchtasks runs schtasks.exe with the given arguments;
    // taskName is for tests.
    public static (bool Succeeded, string Detail) Apply(
        PowerSchedule schedule, string masterPath, DateTime now, Func<string[], (int ExitCode, string Output)> runSchtasks,
        string taskName = TaskName)
    {
        try
        {
            if (schedule.WakeMinute is not int wakeMinute || schedule.Days == 0)
            {
                if (runSchtasks(new[] { "/Query", "/TN", taskName }).ExitCode != 0)
                {
                    return (true, "não liga sozinha");
                }

                var (deleteCode, deleteOutput) = runSchtasks(new[] { "/Delete", "/TN", taskName, "/F" });
                return deleteCode == 0
                    ? (true, "não liga mais sozinha (a tarefa do Windows foi removida)")
                    : (false, $"a tarefa do Windows para ligar a turma não pôde ser removida: {deleteOutput.Trim()}");
            }

            var xmlPath = Path.Combine(Path.GetTempPath(), $"midview-ligar-turma-{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(xmlPath, BuildXml(schedule, masterPath, now), Encoding.Unicode);
                var (createCode, createOutput) = runSchtasks(new[] { "/Create", "/TN", taskName, "/XML", xmlPath, "/F" });
                return createCode == 0
                    ? (true, $"liga às {PowerSchedule.FormatTime(wakeMinute)} ({PowerSchedule.DescribeDays(schedule.Days)}), pedido enviado por este computador")
                    : (false, $"o Windows não criou a tarefa para ligar a turma: {createOutput.Trim()}");
            }
            finally
            {
                File.Delete(xmlPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return (false, $"a tarefa do Windows para ligar a turma não pôde ser criada: {ErrorText.Describe(ex)}");
        }
    }

    public static (int ExitCode, string Output) RunSchtasks(string[] args)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }

    // On time, or at most MaxLateness late (a PC that was off or asleep).
    public static bool ShouldWake(int wakeMinute, DateTime now)
    {
        var late = now - now.Date.AddMinutes(wakeMinute);
        if (late > TimeSpan.FromHours(12))
        {
            late -= TimeSpan.FromDays(1);
        }
        else if (late <= TimeSpan.FromHours(-12))
        {
            late += TimeSpan.FromDays(1);
        }
        return late >= -EarlyTolerance && late <= MaxLateness;
    }

    // What the scheduled task runs. Every run leaves one line in the log,
    // which the schedule window shows ("Última vez: ...").
    public static int RunFromCommandLine(
        string wakeTime, DateTime now, string rosterPath, string logPath,
        Func<IReadOnlyCollection<byte[]>, (int Sent, string? LastError)> send)
    {
        if (!PowerSchedule.TryParseTime(wakeTime, out var wakeMinute))
        {
            Log(logPath, now, $"horário inválido na tarefa do Windows: \"{wakeTime}\"");
            return 2;
        }

        if (!ShouldWake(wakeMinute, now))
        {
            Log(logPath, now, $"não ligou a turma: já tinha passado muito das {PowerSchedule.FormatTime(wakeMinute)} (este computador estava desligado?)");
            return 0;
        }

        List<KnownMachine> machines;
        try
        {
            machines = KnownMachinesFile.Load(rosterPath, out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(logPath, now, $"a lista da sala não pôde ser lida: {ex.Message}");
            return 1;
        }

        var macAddresses = machines
            .Select(machine => WakeOnLan.TryParseMacAddress(machine.MacAddress, out var mac) ? mac : null)
            .OfType<byte[]>()
            .ToList();
        var unknown = machines.Count - macAddresses.Count;
        if (macAddresses.Count == 0)
        {
            Log(logPath, now, "nenhuma máquina da sala tem a placa de rede conhecida ainda");
            return 0;
        }

        var (sent, lastError) = send(macAddresses);
        Log(logPath, now, sent > 0
            ? $"pedido para ligar enviado para {macAddresses.Count} máquina(s)" + (unknown > 0 ? $"; {unknown} sem a placa de rede conhecida" : string.Empty)
            : $"o pedido para ligar não pôde ser enviado ({lastError ?? "sem rede"})");
        return sent > 0 ? 0 : 1;
    }

    public static string? LastRun(string logPath)
    {
        try
        {
            return File.Exists(logPath) ? File.ReadLines(logPath).LastOrDefault(line => line.Length > 0) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Log(string logPath, DateTime now, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, string.Create(CultureInfo.InvariantCulture, $"{now:dd/MM/yyyy HH:mm}: {text}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere else to say it: the task runs without a window.
        }
    }
}
