namespace Midview.Session;

// How long the ScreenFrame capture loop rests between captures. While
// something is moving on the screen (scrolling, a video, a window being
// dragged) or the teacher is clicking and typing, it captures twice as often,
// so what follows shows up sooner; once that has stopped for a second it goes
// back to the old pace, which costs the student's PC half as much. A
// blinking caret or the clock doesn't count as moving: it changes one capture
// and not the next.
public sealed class CaptureCadence
{
    public const int ActiveIntervalMs = 33;
    public const int IdleIntervalMs = 66;
    public const int ActiveForMs = 1000;

    private long _lastActivityMs = long.MinValue / 2;
    private bool _lastCaptureChanged;

    // The teacher's input, from the connection's receiving thread.
    public void Activity(long nowMs) => Interlocked.Exchange(ref _lastActivityMs, nowMs);

    // After each capture, from the capture thread.
    public void Captured(bool changed, long nowMs)
    {
        if (changed && _lastCaptureChanged)
        {
            Activity(nowMs);
        }
        _lastCaptureChanged = changed;
    }

    // workMs is how long the capture, diff and encode just took, elapsedMs
    // that plus the send, which can wait on a slow network without using the
    // CPU. The rest is never shorter than half of the work, so a slow PC
    // spends at most two thirds of one core on capturing instead of all of it.
    public int RestMs(int workMs, int elapsedMs, long nowMs)
    {
        var interval = nowMs - Interlocked.Read(ref _lastActivityMs) < ActiveForMs ? ActiveIntervalMs : IdleIntervalMs;
        return Math.Max(interval - elapsedMs, workMs / 2);
    }
}
