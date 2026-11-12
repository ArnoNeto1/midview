using System.Windows.Threading;

namespace Midview.Master;

// The status bar says how the last thing done from the main window went.
// Its lines are of three kinds, and a window opened from the main window
// (ShowOnce, "Máquinas da sala") treats them apart (ClearEarlierStatus):
// - an action's result ("Telas desbloqueadas."): written directly, it goes
//   when a window opens, as that window goes on in its own text; left there,
//   it read like that window's result;
// - a notice the same click just wrote (machines left out, ShowNotice): stays;
// - a warning (something not read or not saved, ShowWarning): stays until
//   another line takes its place, as it still holds.
// While screens are locked, "Telas bloqueadas: N de M" comes back instead of
// a result; otherwise the selection, as when nothing else is said.
public partial class MainWindow
{
    // True from a notice until the click that wrote it is over: the reset
    // runs at Background priority, after the input being handled.
    private bool _noticeJustWritten;

    // The last warning, while the bar still shows it.
    private string? _warning;

    private void ShowNotice(string notice)
    {
        StatusTextBlock.Text = notice;
        _noticeJustWritten = true;
        Dispatcher.InvokeAsync(() => _noticeJustWritten = false, DispatcherPriority.Background);
    }

    private void ShowWarning(string warning)
    {
        StatusTextBlock.Text = warning;
        _warning = warning;
    }

    private void ClearEarlierStatus()
    {
        var line = StatusTextBlock.Text;
        if (!StatusLine.ClearsOnWindow(line, _noticeJustWritten, _warning))
        {
            return;
        }
        if (_screenLocks.Count > 0)
        {
            ShowScreenLockStatus();
        }
        else
        {
            StatusTextBlock.Text = SelectionStatus();
        }
    }
}

// The rule ClearEarlierStatus goes by, apart so it can be tested.
public static class StatusLine
{
    // Whether line, the status bar's text when a window opens, is an earlier
    // action's result. warning: the last warning written (still the line when
    // they are the same text).
    public static bool ClearsOnWindow(string line, bool noticeJustWritten, string? warning) =>
        !noticeJustWritten && line != warning;
}
