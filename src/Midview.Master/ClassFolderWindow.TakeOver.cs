using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderWindow: "Guardar as pastas das turmas neste computador" on a
// colleague's PC (ClassFolderHandOver, through RemoteClassFolderController):
// a confirmation naming the home PC and what comes from it (or, with that PC's
// Midview not answering, the agreed question saying its classes don't come),
// the copy's progress with "Cancelar" (until the copy is in place: from then
// on it goes to the end), and the result line. Closing the window cancels a
// copy not in place yet. Never while a lesson is on; and only from the home
// the teacher was asked about.
public partial class ClassFolderWindow
{
    // The copy running, for "Cancelar" (null when none is, or it is going in place).
    private HandOverCancel? _takeOverCancel;

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
        var cancel = new HandOverCancel();
        _takeOverCancel = cancel;
        CancelTakeOverButton.IsEnabled = true;
        CancelTakeOverButton.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        // Reported on the copy's thread, shown here; never over "Cancelando" or the result.
        var progress = new Progress<HandOverProgress>(step =>
        {
            if (step.InPlace)
            {
                // Too late to cancel: the room's PCs are being told.
                CancelTakeOverButton.Visibility = Visibility.Collapsed;
                _takeOverCancel = null;
            }
            if (_busy && !cancel.IsCancelled)
            {
                ShowResults(ClassFolderText.TakeOverProgress(home, step), Array.Empty<string>());
            }
        });
        HandOverResult result;
        try
        {
            result = await Task.Run(() => remote.TakeOverAsync(handOver, confirmed, closed, progress, cancel));
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

    // Stops the copy. Once it is going in place it is too late (HandOverCancel
    // says so at once): the button goes, nothing says "Cancelando", and the
    // take-over goes on to the end (the result says how it ended).
    private void OnCancelTakeOverClicked(object sender, RoutedEventArgs e)
    {
        if (_takeOverCancel is not { } cancel)
        {
            return;
        }
        CancelTakeOverButton.IsEnabled = false;
        if (!cancel.Cancel())
        {
            CancelTakeOverButton.Visibility = Visibility.Collapsed;
            _takeOverCancel = null;
            return;
        }
        ShowResults(ClassFolderText.TakeOverCancelling(_controller.HomeName ?? ClassHome.UnnamedText), Array.Empty<string>());
    }
}
