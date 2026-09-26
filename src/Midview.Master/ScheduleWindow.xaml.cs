using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// The lab's power timetable: which days, when it turns on and when it turns
// off. Saving it keeps it in PowerScheduleFile, sets up this PC's wake task
// (WakeTask) and hands the shutdown part to every machine's Agent; a
// machine that's offline gets it later from its tile's poller.
public partial class ScheduleWindow : Window
{
    private readonly Func<IReadOnlyList<KnownMachine>> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private readonly Action<PowerSchedule> _onSaved;

    public ScheduleWindow(
        Func<IReadOnlyList<KnownMachine>> targets, PowerSchedule? current, X509Certificate2 masterCertificate,
        Action<PowerSchedule> onSaved)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        _onSaved = onSaved;
        TargetsTextBlock.Text = $"Na turma toda ({targets().Count} máquinas)";

        var schedule = current ?? new PowerSchedule(0, PowerSchedule.Weekdays, null, null);
        foreach (var (day, box) in DayBoxes)
        {
            box.IsChecked = schedule.IsScheduledOn(day);
        }
        WakeCheckBox.IsChecked = schedule.WakeMinute is not null;
        if (schedule.WakeMinute is int wakeMinute)
        {
            WakeTimeTextBox.Text = PowerSchedule.FormatTime(wakeMinute);
        }
        ShutdownCheckBox.IsChecked = schedule.ShutdownMinute is not null;
        if (schedule.ShutdownMinute is int shutdownMinute)
        {
            ShutdownTimeTextBox.Text = PowerSchedule.FormatTime(shutdownMinute);
        }
        ShowLastRun();
    }

    private (DayOfWeek Day, CheckBox Box)[] DayBoxes => new[]
    {
        (DayOfWeek.Monday, MondayCheckBox), (DayOfWeek.Tuesday, TuesdayCheckBox),
        (DayOfWeek.Wednesday, WednesdayCheckBox), (DayOfWeek.Thursday, ThursdayCheckBox),
        (DayOfWeek.Friday, FridayCheckBox), (DayOfWeek.Saturday, SaturdayCheckBox),
        (DayOfWeek.Sunday, SundayCheckBox),
    };

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var schedule = ReadForm(out var problem);
        if (schedule is null)
        {
            ProgressTextBlock.Text = problem;
            return;
        }

        SaveButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = "Salvando...";
        try
        {
            PowerScheduleFile.Save(PowerScheduleFile.DefaultPath, schedule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProgressTextBlock.Text = $"O agendamento não pôde ser gravado: {ex.Message}";
            SaveButton.IsEnabled = true;
            return;
        }
        _onSaved(schedule);

        var masterPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Midview.Master.exe");
        var wakeTask = Task.Run(() => WakeTask.Apply(schedule, masterPath, DateTime.Now, WakeTask.RunSchtasks));
        var payload = schedule.Encode();
        var machineResults = Task.WhenAll(_targets().Select(machine => Task.Run(() => SendTo(machine, payload))));

        var (wakeSucceeded, wakeDetail) = await wakeTask;
        var results = await machineResults;
        var lines = new[] { $"{(wakeSucceeded ? "✓" : "✗")} Ligar: {wakeDetail}" }
            .Concat(results.Select(result => $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        ResultTextBlock.Text = string.Join("\n", lines);

        ProgressTextBlock.Text = wakeSucceeded && results.All(result => result.Succeeded)
            ? "Agendamento salvo."
            : "Agendamento salvo, mas veja os avisos abaixo.";
        ShowLastRun();
        SaveButton.IsEnabled = true;
    }

    private PowerSchedule? ReadForm(out string problem)
    {
        problem = string.Empty;
        byte days = 0;
        foreach (var (day, box) in DayBoxes)
        {
            if (box.IsChecked == true)
            {
                days |= (byte)(1 << (int)day);
            }
        }

        int? wakeMinute = null;
        if (WakeCheckBox.IsChecked == true)
        {
            if (!PowerSchedule.TryParseTime(WakeTimeTextBox.Text, out var minute))
            {
                problem = "O horário para ligar não é válido (escreva, por exemplo, 07:00).";
                return null;
            }
            wakeMinute = minute;
        }

        int? shutdownMinute = null;
        if (ShutdownCheckBox.IsChecked == true)
        {
            if (!PowerSchedule.TryParseTime(ShutdownTimeTextBox.Text, out var minute))
            {
                problem = "O horário para desligar não é válido (escreva, por exemplo, 18:00).";
                return null;
            }
            shutdownMinute = minute;
        }

        if ((wakeMinute is not null || shutdownMinute is not null) && days == 0)
        {
            problem = "Escolha pelo menos um dia.";
            return null;
        }
        if (wakeMinute is not null && wakeMinute == shutdownMinute)
        {
            problem = "Os horários de ligar e de desligar são iguais.";
            return null;
        }

        return new PowerSchedule(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), days, wakeMinute, shutdownMinute);
    }

    private (string Name, bool Succeeded, string Detail) SendTo(KnownMachine machine, byte[] payload)
    {
        if (machine.AgentThumbprint is not { Length: > 0 } agentThumbprint)
        {
            return (machine.Name, false,
                "falta o thumbprint do Agent (reinstale com o pacote de instalação, ou preencha em \"Testar / bloquear sites...\")");
        }

        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, agentThumbprint, _masterCertificate,
                MessageType.SetPowerSchedule, payload);
            // An Agent from before scheduling answers a bare "no".
            return (machine.Name, succeeded,
                !succeeded && detail.Length == 0 ? "o Midview dessa máquina é de uma versão anterior: reinstale com o pacote atual" : detail);
        }
        catch (Exception ex)
        {
            return (machine.Name, false, $"{FailureText.DescribeAgent(ex)}; ela recebe o agendamento quando o MidviewMaster a vir ligada");
        }
    }

    private void ShowLastRun()
    {
        var lastRun = WakeTask.LastRun(WakeTask.LogPath);
        LastRunTextBlock.Text = lastRun is null ? string.Empty : $"Última vez que a tarefa para ligar rodou: {lastRun}";
    }
}
