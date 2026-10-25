using Xunit;

// Rig PCs whose Agents are busy with their students' folders: each one's
// lesson lock is held (a Reconcile that waits, on a thread of its own) until
// Free, FreeAfter's time, or Dispose. A claim then waits StudentPc.ClaimWait
// and is refused as busy (ClassHomeClaim.BusyText); a lesson sent meanwhile
// waits for the lock.
internal sealed class BusyAgents : IDisposable
{
    private readonly ManualResetEventSlim _free = new();
    private readonly List<Task> _looking = new();

    public BusyAgents(IEnumerable<StudentPc> pcs)
    {
        foreach (var pc in pcs)
        {
            using var holding = new ManualResetEventSlim();
            pc.WhileLooking = () =>
            {
                holding.Set();
                _free.Wait(TimeSpan.FromSeconds(60));
            };
            _looking.Add(Task.Factory.StartNew(
                pc.Agent.Reconcile, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));
            pc.WhileLooking = null;
        }
    }

    public void Free() => _free.Set();

    public void FreeAfter(TimeSpan wait) => _ = Task.Delay(wait).ContinueWith(
        _ =>
        {
            try
            {
                _free.Set();
            }
            catch (ObjectDisposedException)
            {
                // Freed and gone already.
            }
        },
        TaskScheduler.Default);

    public void Dispose()
    {
        _free.Set();
        Assert.True(Task.WaitAll(_looking.ToArray(), TimeSpan.FromSeconds(10)));
        _free.Dispose();
    }
}
