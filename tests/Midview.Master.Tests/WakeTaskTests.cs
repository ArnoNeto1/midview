using System.Text;
using System.Xml.Linq;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class WakeTaskTests : IDisposable
{
    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly DateTime Monday = new(2026, 9, 28);
    private static readonly PowerSchedule Weekdays7am = new(1000, PowerSchedule.Weekdays, 7 * 60, 18 * 60);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"midview-wake-{Guid.NewGuid():N}");

    public WakeTaskTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void BuildXml_RunsTheMasterAtTheWakeTime_OnTheChosenDays()
    {
        var xml = XDocument.Parse(WakeTask.BuildXml(Weekdays7am, @"C:\Users\prof\Midview Professor\Midview.Master.exe", Monday));

        var trigger = xml.Descendants(Task + "CalendarTrigger").Single();
        Assert.Equal("2026-09-28T07:00:00", trigger.Element(Task + "StartBoundary")!.Value);
        Assert.Equal(
            new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
            trigger.Descendants(Task + "DaysOfWeek").Elements().Select(day => day.Name.LocalName));
        Assert.Equal(@"C:\Users\prof\Midview Professor\Midview.Master.exe", xml.Descendants(Task + "Command").Single().Value);
        Assert.Equal("--ligar-turma 07:00", xml.Descendants(Task + "Arguments").Single().Value);
        Assert.Equal("true", xml.Descendants(Task + "WakeToRun").Single().Value);
        Assert.Equal("true", xml.Descendants(Task + "StartWhenAvailable").Single().Value);
        Assert.Equal("InteractiveToken", xml.Descendants(Task + "LogonType").Single().Value);
    }

    [Fact]
    public void Apply_CreatesTheTaskFromAUnicodeXmlFile()
    {
        var calls = new List<string[]>();
        string? xmlFileContent = null;

        var (succeeded, detail) = WakeTask.Apply(Weekdays7am, @"C:\Midview.Master.exe", Monday, args =>
        {
            calls.Add(args);
            xmlFileContent = File.ReadAllText(args[4], Encoding.Unicode);
            return (0, "SUCCESS");
        });

        Assert.True(succeeded);
        Assert.Equal("liga às 07:00 (seg a sex), pedido enviado por este computador", detail);
        var call = Assert.Single(calls);
        Assert.Equal(new[] { "/Create", "/TN", WakeTask.TaskName, "/XML" }, call[..4]);
        Assert.Equal("/F", call[5]);
        Assert.Contains("--ligar-turma 07:00", xmlFileContent);
        Assert.False(File.Exists(call[4])); // the temporary file is gone
    }

    [Fact]
    public void Apply_WindowsRefusing_IsReportedWithItsMessage()
    {
        var (succeeded, detail) = WakeTask.Apply(Weekdays7am, @"C:\Midview.Master.exe", Monday, _ => (1, "ERRO: Acesso negado.\r\n"));

        Assert.False(succeeded);
        Assert.EndsWith("ERRO: Acesso negado.", detail);
    }

    [Fact]
    public void Apply_NoWakeTime_RemovesAnExistingTask()
    {
        var calls = new List<string[]>();

        var (succeeded, _) = WakeTask.Apply(Weekdays7am with { WakeMinute = null }, @"C:\Midview.Master.exe", Monday, args =>
        {
            calls.Add(args);
            return (0, string.Empty);
        });

        Assert.True(succeeded);
        Assert.Equal(new[] { "/Query", "/Delete" }, calls.Select(args => args[0]));
    }

    [Fact]
    public void Apply_NoWakeTimeAndNoTask_DeletesNothing()
    {
        var calls = new List<string[]>();

        var (succeeded, detail) = WakeTask.Apply(Weekdays7am with { WakeMinute = null }, @"C:\Midview.Master.exe", Monday, args =>
        {
            calls.Add(args);
            return (1, "ERRO: O sistema não pode encontrar o arquivo especificado.");
        });

        Assert.True(succeeded);
        Assert.Equal("não liga sozinha", detail);
        Assert.Equal("/Query", Assert.Single(calls)[0]);
    }

    // The real thing, where there is one: Windows accepts the definition,
    // and removing it works.
    [Fact]
    public void Apply_OnWindows_RegistersAndRemovesARealTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = $"Midview teste {Guid.NewGuid():N}";
        var masterPath = Environment.ProcessPath!;
        try
        {
            var (created, createDetail) = WakeTask.Apply(Weekdays7am, masterPath, DateTime.Now, WakeTask.RunSchtasks, name);
            Assert.True(created, createDetail);
            Assert.Equal(0, WakeTask.RunSchtasks(new[] { "/Query", "/TN", name }).ExitCode);

            var (removed, removeDetail) = WakeTask.Apply(
                Weekdays7am with { WakeMinute = null }, masterPath, DateTime.Now, WakeTask.RunSchtasks, name);
            Assert.True(removed, removeDetail);
            Assert.NotEqual(0, WakeTask.RunSchtasks(new[] { "/Query", "/TN", name }).ExitCode);
        }
        finally
        {
            WakeTask.RunSchtasks(new[] { "/Delete", "/TN", name, "/F" });
        }
    }

    [Theory]
    [InlineData(7.0, true)] // on time
    [InlineData(6.95, true)] // Windows a moment early
    [InlineData(7.9, true)] // this PC was asleep or off, less than an hour late
    [InlineData(8.5, false)] // turned on much later
    [InlineData(6.5, false)]
    public void ShouldWake_OnlyAroundTheWakeTime(double hour, bool expected) =>
        Assert.Equal(expected, WakeTask.ShouldWake(7 * 60, Monday.AddHours(hour)));

    [Fact]
    public void RunFromCommandLine_WakesEveryMachineWithAKnownCard_AndLogsIt()
    {
        var roster = Path.Combine(_directory, "known-machines.json");
        var log = Path.Combine(_directory, "ligar-turma.log");
        KnownMachinesFile.Save(roster, new[]
        {
            new KnownMachine("Aluno-1", "10.0.0.1", "S1", "A1", "00-1A-2B-3C-4D-5E"),
            new KnownMachine("Aluno-2", "10.0.0.2", "S2", "A2"),
        });
        IReadOnlyCollection<byte[]>? sent = null;

        var exitCode = WakeTask.RunFromCommandLine("07:00", Monday.AddHours(7), roster, log, macAddresses =>
        {
            sent = macAddresses;
            return (macAddresses.Count * 3, null);
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(new byte[] { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E }, Assert.Single(sent!));
        Assert.Equal(
            "28/09/2026 07:00: pedido para ligar enviado para 1 máquina(s); 1 sem a placa de rede conhecida",
            WakeTask.LastRun(log));
    }

    [Fact]
    public void RunFromCommandLine_TooLate_SendsNothing()
    {
        var log = Path.Combine(_directory, "ligar-turma.log");
        var sentAnything = false;

        WakeTask.RunFromCommandLine("07:00", Monday.AddHours(15), Path.Combine(_directory, "none.json"), log, _ =>
        {
            sentAnything = true;
            return (1, null);
        });

        Assert.False(sentAnything);
        Assert.Contains("não ligou a turma", WakeTask.LastRun(log));
    }

    [Fact]
    public void LastRun_NoLogYet_IsNull() =>
        Assert.Null(WakeTask.LastRun(Path.Combine(_directory, "missing.log")));
}
