using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// RemoteClassFolderController with the home's Master closed, and coming back:
// what the remote shows and says around an end it made on the Agents.
public sealed partial class RemoteClassFolderControllerTests
{
    // The home's Master opened again right after this PC ended its lesson on
    // the Agents: until the home's own state has taken that end in, the remote
    // shows the end, not the home's older word on the lesson.
    [Fact]
    public async Task AHomeBackRightAfterAnEndMadeOnTheAgents_ShowsTheEnd_NotTheStaleLesson()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig);
        await lab.Remote.StartAsync(Turma);
        var lesson = lab.Home.Lesson;
        lab.CloseHome();
        await PollUntilClosed(lab.Remote);
        await lab.Remote.EndAsync(Turma, EndChoice.Keep);
        var ended = lesson with { SavedAt = lesson.SavedAt + 1, Ended = true };

        lab.ReopenHost(); // its window's controller still has the lesson on
        await lab.Remote.PollAsync();

        Assert.False(lab.Remote.HomeClosed);
        Assert.True(lab.Home.Lesson.IsOn);
        Assert.Equal(ended, lab.Remote.Lesson);

        var home = lab.ReopenHome(); // its sync, opened again, takes the end in
        await Until(() => Task.FromResult(home.Lesson == ended));
        await lab.Remote.PollAsync();
        Assert.Equal(ended, lab.Remote.Lesson);

        // The home's next lesson shows as it is.
        await home.StartAsync("3º B");
        await lab.Remote.PollAsync();
        Assert.Equal(home.Lesson, lab.Remote.Lesson);
        Assert.Equal("3º B", lab.Remote.Lesson.Turma);
    }

    // A PC the grid shows off isn't tried: its line says so, the end counts
    // it, and the headline doesn't promise it the end when it is switched on
    // (nothing hands it over until the home's Midview opens, or the day ends).
    [Fact]
    public async Task WithTheHomeClosed_AnEndWithAPcOff_SaysWhichAndDoesNotPromiseIt()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new TwoMasters(rig);
        await lab.Remote.StartAsync(Turma);
        lab.CloseHome();
        await PollUntilClosed(lab.Remote);
        rig.Offline["PC3"] = true;

        var ended = await lab.Remote.EndAsync(Turma, EndChoice.Keep);

        Assert.Equal(AllPcs, ended.Select(line => line.Name));
        Assert.Equal(new[] { true, true, false }, ended.Select(line => line.Ok));
        Assert.Equal(FailureText.Offline, ended[2].Detail);
        Assert.False(rig.Pcs[0].Agent.Current.IsOn);
        Assert.True(rig.Pcs[2].Agent.Current.IsOn);
        // What the window's headline says of it.
        Assert.Equal(
            "A aula do 3º A foi encerrada em 2 de 3 PCs; os desligados continuam na aula até o Midview do PC PROF-1 abrir ou o dia acabar.",
            ClassFolderText.Outcome(
                started: false, Turma, ended.Count(line => line.Ok), ended.Count,
                ended.Count(line => !line.Ok && line.Detail == FailureText.Offline), lab.Remote.HomeName));
    }

    // Something at the home's address that takes the connection and drops it
    // before any answer (a hung home, a reset handshake) is neutral: it doesn't
    // open a home found closed.
    [Fact]
    public async Task AClosedHome_IsNotOpenedAgain_ByConnectionsCutOffBeforeAnyAnswer()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig);
        await lab.Remote.PollAsync();
        lab.CloseHome();
        await PollUntilClosed(lab.Remote);

        var dropper = new TcpListener(IPAddress.Loopback, 0);
        dropper.Start();
        var dropping = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    (await dropper.AcceptTcpClientAsync()).Dispose();
                }
            }
            catch (Exception)
            {
                // Stopped.
            }
        });
        lab.PointHomeAt(((IPEndPoint)dropper.LocalEndpoint).Port);
        try
        {
            for (var poll = 0; poll < RemoteClassFolderController.ClosedAfter; poll++)
            {
                await lab.Remote.PollAsync();
                Assert.True(lab.Remote.HomeClosed);
            }
        }
        finally
        {
            dropper.Stop();
            await dropping;
        }
    }
}
