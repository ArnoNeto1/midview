namespace Midview.Session;

// The ScreenFrame updates on their way to the viewer that it hasn't
// acknowledged yet (FrameAck). The capture loop waits for room before
// capturing the next one, so a slow network or a busy teacher PC gets fewer,
// newer updates instead of a queue of old ones: whatever changes in the
// meantime goes out together in the next update, since FramePipeline diffs
// against the last frame it sent.
public sealed class FrameCredit
{
    public const int MaxUnacknowledged = 2;

    private readonly object _gate = new();

    // Per update, the id of its last message: an update split into several
    // messages (ScreenFrame.Split) is acknowledged once the viewer has drawn
    // all of them.
    private readonly Queue<(int Id, long SentAtMs)> _unacknowledged = new();
    private int _lastId;

    // A new update is about to go out, as this many messages: the id of the
    // first, the rest numbered on from it, each acknowledged by the viewer.
    public int Send(long nowMs, int messages = 1)
    {
        lock (_gate)
        {
            var firstId = _lastId + 1;
            _lastId += messages;
            _unacknowledged.Enqueue((_lastId, nowMs));
            return firstId;
        }
    }

    // The viewer has drawn every message up to frameId. How long the update
    // ending with frameId took from being sent to being acknowledged, or null
    // when frameId completes no update still waiting (a repeat, part of a
    // split update, or one already given up on).
    public long? Acknowledge(int frameId, long nowMs)
    {
        lock (_gate)
        {
            long? sentAtMs = null;
            while (_unacknowledged.Count > 0 && _unacknowledged.Peek().Id <= frameId)
            {
                var (id, sentAt) = _unacknowledged.Dequeue();
                if (id == frameId)
                {
                    sentAtMs = sentAt;
                }
            }
            Monitor.PulseAll(_gate);
            return nowMs - sentAtMs;
        }
    }

    // Returns once fewer than MaxUnacknowledged updates are waiting: true
    // then, false as soon as cancellationToken is cancelled (the connection
    // is closing, so nothing more should be captured). A viewer that stops
    // acknowledging mustn't freeze the picture for good, so after timeout the
    // oldest one is given up on and the loop carries on.
    public bool WaitForRoom(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        using var wakeOnCancel = cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                Monitor.PulseAll(_gate);
            }
        });

        lock (_gate)
        {
            while (_unacknowledged.Count >= MaxUnacknowledged && !cancellationToken.IsCancellationRequested)
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0)
                {
                    _unacknowledged.Dequeue();
                    break;
                }

                // Woken early only by an acknowledgement or the cancellation.
                Monitor.Wait(_gate, (int)Math.Min(left, int.MaxValue));
            }
            return !cancellationToken.IsCancellationRequested;
        }
    }
}
