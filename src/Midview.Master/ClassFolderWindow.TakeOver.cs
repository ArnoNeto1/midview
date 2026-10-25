using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderWindow: "Guardar as pastas das turmas neste computador" on a
// colleague's PC (ClassFolderHandOver, through RemoteClassFolderController):
// a confirmation naming the home PC and what comes from it (or, with that PC's
// Midview not answering, the agreed question saying its classes don't come),
// the copy's progress with "Cancelar", and the result line. Never while a
// lesson is on; and only from the home the teacher was asked about.
public partial class ClassFolderWindow
{
    // The copy running, for "Cancelar" (null when none is).
    private CancellationTokenSource? _takeOverCancel;

    // Shown on a colleague's PC (a remote controller, and this PC's hand-over);
    // disabled while a lesson is on, its tooltip saying why.
    private void UpdateTakeOverButton()
    {
        var shown = _handOver is not null && _controller is RemoteClassFolderController;
        var lessonOn = Lesson.IsOn;
        TakeOverButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        TakeOverButton.IsEnabled = shown && !_busy && !lessonOn;
        TakeOverButton.ToolTip = lessonOn ? ClassFolderText.EndLessonFirst : null;
    }

    private async void OnTakeOverClicked(object sender, RoutedEventArgs e)
    {
        if (_handOver is not { } handOver || _controller is not RemoteClassFolderController remote || Lesson.IsOn)
        {
            ShowView();
            return;
        }

        // The home asked about: the take-over is refused if the room keeps another one by then.
        var confirmed = remote.Home;
        var home = _controller.HomeName ?? ClassHome.UnnamedText;
        var closed = _controller.HomeClosed;
        string question;
        if (closed)
        {
            question = ClassFolderText.TakeOverUnreachableQuestion(home);
        }
        else
        {
            // What the home's window lists, read now, and whether this PC has classes of its own.
            if (await ReadAsync(lessonFiles: false) is not { } read)
            {
                ShowResults(_controller.Note ?? ClassFolderText.HomeNoAnswer(home), Array.Empty<string>());
                return;
            }
            var ownClasses = await Task.Run(handOver.OwnClassCount) > 0;
            question = ClassFolderText.TakeOverQuestion(
                home, read.Classes.Count, read.Classes.Sum(summary => summary.Files), read.Classes.Sum(summary => summary.Bytes),
                handOver.MachineName, ownClasses);
        }
        if (MessageBox.Show(this, question, ClassFolderText.TakeOverButton, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        CloseNameRow();
        SetBusy(true, closed ? ClassFolderText.TakeOverUnreachableProgress(home) : ClassFolderText.TakeOverProgress(home, default));
        using var cancel = new CancellationTokenSource();
        _takeOverCancel = cancel;
        CancelTakeOverButton.IsEnabled = true;
        CancelTakeOverButton.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        // Reported on the copy's thread, shown here; never over "Cancelando" or the result.
        var progress = new Progress<HandOverProgress>(step =>
        {
            if (_busy && !cancel.IsCancellationRequested)
            {
                ShowResults(ClassFolderText.TakeOverProgress(home, step), Array.Empty<string>());
            }
        });
        HandOverResult result;
        try
        {
            result = await Task.Run(() => remote.TakeOverAsync(handOver, confirmed, closed, progress, cancel.Token));
        }
        catch (Exception ex)
        {
            // Not from the take-over itself (it never throws): how it ended can't be told.
            result = new HandOverResult(false, ClassFolderText.TakeOverUnknown(ErrorText.Describe(ex)));
        }
        finally
        {
            _takeOverCancel = null;
            CancelTakeOverButton.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
        ShowView();
        ShowResults(result.Detail, Array.Empty<string>());
        RefreshFromDisk();
    }

    // Stops the copy; once it is in place the take-over goes on to the end (the result says how it ended).
    private void OnCancelTakeOverClicked(object sender, RoutedEventArgs e)
    {
        if (_takeOverCancel is not { } cancel)
        {
            return;
        }
        cancel.Cancel();
        CancelTakeOverButton.IsEnabled = false;
        ShowResults(ClassFolderText.TakeOverCancelling(_controller.HomeName ?? ClassHome.UnnamedText), Array.Empty<string>());
    }
}
