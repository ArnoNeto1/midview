namespace Midview.Session;

// Keeps the student's clipboard in step with the controlling teacher's while
// a control session is on. One reader side (the control loop) calls
// ApplyRemote with text the teacher sent; one writer side (the capture
// thread) calls PollLocal and sends back whatever it returns. Only text, only
// up to ClipboardText.MaxBytes, and only while controlling.
//
// The last value synced (sent or received) is remembered so a value just
// applied isn't echoed straight back, which would loop forever.
public sealed class ClipboardBridge
{
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromMilliseconds(700);

    private readonly IClipboard _clipboard;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private string? _lastSynced;
    private DateTime _lastPoll = DateTime.MinValue;
    private volatile bool _controlling;

    public ClipboardBridge(IClipboard clipboard, Func<DateTime>? utcNow = null)
    {
        _clipboard = clipboard;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // Set by the control loop on StartControl/StopControl: sync runs only
    // while the teacher is actually controlling, not merely watching.
    public bool Controlling
    {
        get => _controlling;
        set => _controlling = value;
    }

    // Reader side: text the teacher copied, to put on the student's clipboard.
    public void ApplyRemote(string text)
    {
        lock (_gate)
        {
            if (text == _lastSynced)
            {
                return;
            }
            _lastSynced = text;
        }
        try
        {
            _clipboard.SetText(text);
        }
        catch (Exception)
        {
            // Clipboard busy or refused: nothing to do, the next change wins.
        }
    }

    // Writer side: the student's clipboard text to send to the teacher, or
    // null when there is nothing new to send (not controlling, polled too
    // recently, empty, too big, or the same as the last value synced).
    public string? PollLocal()
    {
        lock (_gate)
        {
            if (!_controlling)
            {
                return null;
            }
            var now = _utcNow();
            if (now - _lastPoll < MinPollInterval)
            {
                return null;
            }
            _lastPoll = now;
        }

        string? text;
        try
        {
            text = _clipboard.GetText();
        }
        catch (Exception)
        {
            return null;
        }
        // Skip invalid UTF-16 (a lone surrogate) too: encoding it strictly
        // on the send would throw on the capture thread and drop control.
        if (string.IsNullOrEmpty(text)
            || !Midview.Protocol.ClipboardText.TryEncode(text, out var bytes)
            || bytes.Length > ClipboardText_MaxBytes)
        {
            return null;
        }

        lock (_gate)
        {
            if (text == _lastSynced)
            {
                return null;
            }
            _lastSynced = text;
        }
        return text;
    }

    private const int ClipboardText_MaxBytes = Midview.Protocol.ClipboardText.MaxBytes;
}
