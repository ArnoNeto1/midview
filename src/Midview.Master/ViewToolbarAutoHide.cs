namespace Midview.Master;

// When the viewer's floating toolbar shows. It starts shown (the picture is
// still on its way), slides away once the picture is in and the mouse is off
// it, and comes back when the mouse rests on the top edge (the way back to the
// buttons in full screen) or leaves the window. Time comes in from outside,
// so the window drives it with a timer and the tests with plain values.
internal sealed class ViewToolbarAutoHide
{
    public static readonly TimeSpan FirstHideDelay = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(300);

    private bool _pictureIn;
    private DateTime? _hideAt;
    private DateTime? _revealAt;

    public bool Shown { get; private set; } = true;

    // Whether Update has something left to do later.
    public bool Waiting => _hideAt is not null || _revealAt is not null;

    // Only the first picture counts: later ones are new resolutions.
    public void PictureArrived(DateTime now, bool mouseOnToolbar)
    {
        if (_pictureIn)
        {
            return;
        }
        _pictureIn = true;
        if (Shown && !mouseOnToolbar)
        {
            _hideAt = now + FirstHideDelay;
        }
    }

    public void MouseMoved(DateTime now, bool atTopEdge, bool onToolbar)
    {
        if (Shown)
        {
            _revealAt = null;
            if (onToolbar || !_pictureIn)
            {
                _hideAt = null;
            }
            else
            {
                _hideAt ??= now + HideDelay;
            }
            return;
        }

        _hideAt = null;
        _revealAt = atTopEdge ? _revealAt ?? now + RevealDelay : null;
    }

    public void MouseLeftWindow()
    {
        _hideAt = null;
        _revealAt = null;
        Shown = true;
    }

    // Applies whatever is due; true when Shown changed.
    public bool Update(DateTime now)
    {
        if (_hideAt is { } hideAt && now >= hideAt)
        {
            _hideAt = null;
            if (Shown)
            {
                Shown = false;
                return true;
            }
        }
        if (_revealAt is { } revealAt && now >= revealAt)
        {
            _revealAt = null;
            if (!Shown)
            {
                Shown = true;
                return true;
            }
        }
        return false;
    }
}
