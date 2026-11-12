using System.Windows.Threading;

namespace Midview.Master;

// The status bar says how the last thing done from the main window went
// ("Telas desbloqueadas."). A window opened from it (ShowOnce, "Máquinas da
// sala") goes on in its own text, so the line of an earlier action is taken
// away when it opens: left there, it read like that window's result. The bar
// then shows the selection, as it does when nothing else is said. A notice
// the same click wrote just before (machines left out, ShowNotice) stays.
public partial class MainWindow
{
    // True from a notice until the click that wrote it is over: the reset
    // runs at Background priority, after the input being handled.
    private bool _noticeJustWritten;

    private void ShowNotice(string notice)
    {
        StatusTextBlock.Text = notice;
        _noticeJustWritten = true;
        Dispatcher.InvokeAsync(() => _noticeJustWritten = false, DispatcherPriority.Background);
    }

    private void ClearEarlierStatus()
    {
        if (!_noticeJustWritten)
        {
            StatusTextBlock.Text = SelectionStatus();
        }
    }
}
