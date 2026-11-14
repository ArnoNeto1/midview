using System.Windows;

namespace Midview.Master;

// "Travar o toque do aluno" (MessageType.LockStudentTouch): a phone's or a
// tablet's only, on by default. It goes right after StartControl and again
// whenever the teacher changes it while controlling; the student's touch and
// keys are free again as soon as control ends (the phone sees to that).
public partial class ViewControlWindow
{
    private bool TouchLocked => LockTouchCheckBox.IsChecked == true;

    private void SendTouchLock()
    {
        if (_isPhone)
        {
            _client.SendLockStudentTouch(TouchLocked);
        }
    }

    // Changed while controlling: the phone hears it at once; otherwise it
    // goes with the next "Controlar".
    private void OnLockTouchChanged(object sender, RoutedEventArgs e)
    {
        if (_client is null)
        {
            // Checked by the XAML before the constructor set the window up.
            return;
        }
        if (_controlling)
        {
            RunOrCloseOnFailure(SendTouchLock);
        }
        ShowMode();
        ScreenImage.Focus();
    }
}
