using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// The lab's power timetable: which days, when it turns on and when it turns
// off. Saving it keeps it in PowerScheduleFile, sets up this PC's wake task
// (WakeTask) and hands the shutdown part to every machine's Agent (a phone's
// or tablet's turns its screen off then: ScheduleText); a machine that's
// offline gets it later from its tile's poller.
public partial class ScheduleWindow : MidviewWindow
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
        var reached = targets();
        TargetsTextBlock.Text = ScheduleText.Targets(reached);
        AndroidHintTextBlock.Text = ScheduleText.AndroidHint;
        AndroidHintTextBlock.Visibility = DeviceSupport.HasAndroid(reached) ? Visibility.Visible : Visibility.Collapsed;

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

        // The ruler follows the switches and the times, however they change.
        foreach (var box in new[] { WakeCheckBox, ShutdownCheckBox })
        {
            box.Checked += (_, _) => UpdateTimeline();
            box.Unchecked += (_, _) => UpdateTimeline();
        }
        WakeTimeTextBox.TextChanged += (_, _) => UpdateTimeline();
        ShutdownTimeTextBox.TextChanged += (_, _) => UpdateTimeline();
        UpdateTimeline();
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
            ProgressTextBlock.Text = $"O agendamento não pôde ser gravado: {ErrorText.Describe(ex)}";
            SaveButton.IsEnabled = true;
            return;
        }
        _onSaved(schedule);

        var masterPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Midview.Master.exe");
        var wakeTask = Task.Run(() => WakeTask.Apply(schedule, masterPath, DateTime.Now, WakeTask.RunSchtasks));
        var payload = schedule.Encode();
        var machines = _targets();
        var machineResults = Task.WhenAll(machines.Select(machine => Task.Run(() => SendTo(machine, payload))));

        var (wakeTaskSucceeded, wakeDetail) = await wakeTask;
        var results = await machineResults;
        var (wakeSucceeded, wakeLine) = ScheduleText.WakeLine(wakeTaskSucceeded, wakeDetail, schedule.WakeMinute is not null, machines);
        var lines = new[] { wakeLine }
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

    // A phone's or tablet's agent takes it on the same port as a PC's Agent.
    private (string Name, bool Succeeded, string Detail) SendTo(KnownMachine machine, byte[] payload)
    {
        var (succeeded, detail) = ScheduleText.Send(machine, agentThumbprint => CommandClient.SendCommandForResult(
            machine.HostAddress, ConnectionClient.AgentPort, agentThumbprint, _masterCertificate,
            MessageType.SetPowerSchedule, payload));
        return (machine.Name, succeeded, detail);
    }

    private void ShowLastRun()
    {
        var lastRun = WakeTask.LastRun(WakeTask.LogPath);
        LastRunTextBlock.Text = lastRun is null ? string.Empty : $"Última vez que a tarefa para ligar rodou: {lastRun}";
    }

    // The title beside a switch ("Ligar às") flips it, as a check box's own label does.
    private void OnSwitchTitleClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CheckBox { IsEnabled: true } box })
        {
            box.Focus();
            box.IsChecked = box.IsChecked != true;
            e.Handled = true;
        }
    }

    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TimelineClip.Rect = new Rect(0, 0, TimelineTrack.ActualWidth, TimelineTrack.ActualHeight);
        UpdateTimeline();
    }

    // Draws the on stretches over the day ruler. A time counts only while its
    // switch is on and its text is a valid time; an invalid one is left out
    // here and reported when saving, as before.
    private void UpdateTimeline()
    {
        var (segments, text) = ScheduleTimeline.For(ReadTime(WakeCheckBox, WakeTimeTextBox), ReadTime(ShutdownCheckBox, ShutdownTimeTextBox));
        TimelineTextBlock.Text = text;
        var width = TimelineTrack.ActualWidth;
        var bars = new[] { TimelineFirstSegment, TimelineSecondSegment };
        for (var i = 0; i < bars.Length; i++)
        {
            if (i < segments.Count)
            {
                var left = segments[i].Start * width;
                bars[i].Margin = new Thickness(left, 3, 0, 3);
                bars[i].Width = Math.Max(0, (segments[i].End - segments[i].Start) * width);
                bars[i].Visibility = Visibility.Visible;
            }
            else
            {
                bars[i].Visibility = Visibility.Collapsed;
            }
        }
    }

    private static int? ReadTime(CheckBox on, TextBox box) =>
        on.IsChecked == true && PowerSchedule.TryParseTime(box.Text, out var minute) ? minute : null;
}
