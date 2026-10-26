using Xunit;

// Rig PCs whose Agents are busy with their students' folders: each one's
// lesson lock is held (a Reconcile that waits, on a thread of its own) until
// Free, FreeAfter's time, or Dispose. A claim then waits the rig's ClaimWait
// and is refused as busy (ClassHomeClaim.BusyText); a lesson sent meanwhile
// waits for the lock. Their rig is made with ClaimWait, so that comes quickly.
internal sealed class BusyAgents : IDisposable
{
    // The lock wait of a rig whose Agents a test holds busy.
    public static readonly TimeSpan ClaimWait = TimeSpan.FromSeconds(1);

    private readonly ManualResetEventSlim _free = new();
    private readonly List<Task> _looking = new();

    public BusyAgents(IEnumerable<StudentPc> pcs)
    {
        foreach (var pc in pcs)
        {
            Assert.Equal(ClaimWait, pc.ClaimWait);
            using var holding = new ManualResetEventSlim();
            pc.WhileLooking = () =>
            {
                holding.Set();
                _free.Wait(TimeSpan.FromSeconds(60));
            };
            _looking.Add(Task.Factory.StartNew(
                pc.Agent.Reconcile, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            Assert.True(holding.Wait(TimeSpan.FromSeconds(30)));
            pc.WhileLooking = null;
        }
    }

    public void Free() => _free.Set();

    // On a thread of its own: never the pool's, which may be busy with the claims themselves.
    public void FreeAfter(TimeSpan wait) => new Thread(() =>
    {
        Thread.Sleep(wait);
        try
        {
            _free.Set();
        }
        catch (ObjectDisposedException)
        {
            // Freed and gone already.
        }
    })
    { IsBackground = true }.Start();

    public void Dispose()
    {
        _free.Set();
        Assert.True(Task.WaitAll(_looking.ToArray(), TimeSpan.FromSeconds(30)));
        _free.Dispose();
    }
}
