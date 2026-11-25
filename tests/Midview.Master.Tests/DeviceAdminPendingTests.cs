using System.IO;
using System.Net.Sockets;
using Midview.Master;
using Xunit;

public sealed class DeviceAdminPendingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-admin-pending-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // The devices the password didn't reach are kept for when they show up,
    // across a restart of the Master.
    [Fact]
    public void TheDevicesLeftOut_AreKept()
    {
        DeviceAdminPendingFile.Save(new[] { "AAAA", "bbbb" }, _folder);

        Assert.Equal(new[] { "AAAA", "BBBB" }, DeviceAdminPendingFile.Load(_folder).OrderBy(agent => agent));
    }

    [Fact]
    public void NothingSaved_NobodyWaits() => Assert.Empty(DeviceAdminPendingFile.Load(_folder));

    [Fact]
    public void ADeviceThatGotIt_LeavesTheList()
    {
        DeviceAdminPendingFile.Save(new[] { "AAAA", "BBBB" }, _folder);

        DeviceAdminPendingFile.Remove("aaaa", _folder);

        Assert.Equal(new[] { "BBBB" }, DeviceAdminPendingFile.Load(_folder));
    }

    // A new password: only the ones the new one didn't reach wait.
    [Fact]
    public void SavingAgain_ReplacesTheList()
    {
        DeviceAdminPendingFile.Save(new[] { "AAAA" }, _folder);

        DeviceAdminPendingFile.Save(new[] { "CCCC" }, _folder);

        Assert.Equal(new[] { "CCCC" }, DeviceAdminPendingFile.Load(_folder));
    }

    [Fact]
    public void ADeviceThatTakesIt_IsDone() =>
        Assert.Equal(DeviceAdminArrival.Outcome.Put, DeviceAdminArrival.Put(() => (true, "")));

    // It answered but said no, or its Midview is too old: trying again
    // every few seconds would change nothing, so it waits no more.
    [Fact]
    public void ADeviceThatSaysNo_OrTooOld_WaitsNoMore()
    {
        Assert.Equal(DeviceAdminArrival.Outcome.Refused, DeviceAdminArrival.Put(() => (false, "erro")));
        Assert.Equal(DeviceAdminArrival.Outcome.TooOld, DeviceAdminArrival.Put(() => null));
        Assert.False(DeviceAdminArrival.KeepsWaiting(DeviceAdminArrival.Outcome.Refused));
        Assert.False(DeviceAdminArrival.KeepsWaiting(DeviceAdminArrival.Outcome.TooOld));
    }

    // Online a moment ago but not answering now: tried again later.
    [Fact]
    public void ADeviceThatDoesntAnswer_KeepsWaiting()
    {
        var outcome = DeviceAdminArrival.Put(() => throw new SocketException((int)SocketError.TimedOut));

        Assert.Equal(DeviceAdminArrival.Outcome.NoAnswer, outcome);
        Assert.True(DeviceAdminArrival.KeepsWaiting(outcome));
    }
}
