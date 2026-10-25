using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderWindow: "Guardar as pastas das turmas neste computador" on a
// colleague's PC (ClassFolderHandOver, through RemoteClassFolderController):
// a confirmation naming the home PC and what comes from it (or, with that PC's
// Midview not answering, the agreed question saying its classes don't come),
// the copy's progress, and the result line. Never while a lesson is on.
public partial class ClassFolderWindow
{
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
        // Reported on the copy's thread, shown here; never over the result.
        var progress = new Progress<HandOverProgress>(step =>
        {
            if (_busy)
            {
                ShowResults(ClassFolderText.TakeOverProgress(home, step), Array.Empty<string>());
            }
        });
        HandOverResult result;
        try
        {
            result = await Task.Run(() => remote.TakeOverAsync(handOver, closed, progress));
        }
        catch (Exception ex)
        {
            var reason = ErrorText.Describe(ex);
            result = new HandOverResult(
                false, closed ? ClassFolderText.TakeOverUnreachableFailed(reason) : ClassFolderText.TakeOverFailed(home, reason));
        }
        finally
        {
            SetBusy(false);
        }
        ShowView();
        ShowResults(result.Detail, Array.Empty<string>());
        RefreshFromDisk();
    }
}
