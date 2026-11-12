using Midview.Master;
using Midview.Protocol;
using Xunit;

// A phone's poller hands it the class's phone app block and watches what it
// follows, like a PC's does with the program block.
public partial class ThumbnailPollerTests
{
    [Fact]
    public async Task PhoneAppBlock_IsHandedToThePhoneOnce_AndAgainWhenItChanges_NeverThePcBlock()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "ok")));
        var phonePolicy = new PhoneAppPolicy(1000, AppBlockMode.BlockList, new[] { "com.google.android.youtube" });
        var pcPolicy = new AppBlockPolicy(500, AppBlockMode.BlockList, new[] { "chrome.exe" });
        var phone = session.Machine() with { AgentThumbprint = agent.Thumbprint, Kind = DeviceKind.Android };
        using var poller = new ThumbnailPoller(
            phone, session.MasterCertificate, session.Port, agent.Port,
            appBlockPolicy: () => pcPolicy, agentRetryInterval: RetryAtOnce, phoneAppPolicy: () => phonePolicy);
        var polls = new Polls(poller);
        poller.Start();

        await WaitUntil(() => agent.PhoneBlocksReceived.Length > 0);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L }, agent.PhoneBlocksReceived);

        // Lifting is a newer policy like any other.
        phonePolicy = PhoneAppPolicy.Lifted(2000);
        await WaitUntil(() => agent.PhoneBlocksReceived.Length > 1);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L, 2000L }, agent.PhoneBlocksReceived);
        Assert.Empty(agent.BlocksReceived);
    }

    [Fact]
    public async Task PhoneAppBlock_NeverGoesToAPc()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "ok")));
        var pcPolicy = new AppBlockPolicy(500, AppBlockMode.BlockList, new[] { "chrome.exe" });
        var phonePolicy = new PhoneAppPolicy(1000, AppBlockMode.BlockList, new[] { "com.google.android.youtube" });
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            appBlockPolicy: () => pcPolicy, agentRetryInterval: RetryAtOnce, phoneAppPolicy: () => phonePolicy);
        var polls = new Polls(poller);
        poller.Start();

        await WaitUntil(() => agent.BlocksReceived.Length > 0);
        await polls.OneMoreAsync();

        Assert.Empty(agent.PhoneBlocksReceived);
    }

    // An older phone agent answers "Ainda não funciona em celular.": not
    // delivered, so it is tried again after a while (here at once).
    [Fact]
    public async Task PhoneAppBlock_APhoneThatDidNotTakeIt_IsTriedAgain()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate,
            new Message(MessageType.CommandAck, CommandResult.Encode(false, "Ainda não funciona em celular.")));
        var phonePolicy = new PhoneAppPolicy(1000, AppBlockMode.BlockList, new[] { "com.google.android.youtube" });
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint, Kind = DeviceKind.Android },
            session.MasterCertificate, session.Port, agent.Port,
            agentRetryInterval: TimeSpan.Zero, phoneAppPolicy: () => phonePolicy);

        poller.Start();

        await WaitUntil(() => agent.PhoneBlocksReceived.Length >= 2);
    }

    [Fact]
    public async Task PhoneAppBlockObserved_ReportsWhatThePhoneFollows()
    {
        using var session = new LoopbackSession();
        var followed = new PhoneAppPolicy(1000, AppBlockMode.AllowOnly, new[] { "com.android.chrome" });
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "ok")))
        {
            PhoneStatusReply = new Message(MessageType.PhoneAppBlockStatus, followed.Encode()),
        };
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint, Kind = DeviceKind.Android },
            session.MasterCertificate, session.Port, agent.Port);
        var observed = new TaskCompletionSource<PhoneAppBlockClient.StatusResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.PhoneAppBlockObserved += status =>
        {
            if (status is not null)
            {
                observed.TrySetResult(status);
            }
        };
        var pcObserved = false;
        poller.AppBlockObserved += _ => pcObserved = true;

        poller.Start();
        var status = await observed.Task.WaitAsync(ObservedWithin);

        Assert.False(status.OldAgent);
        Assert.True(followed.SameRulesAs(status.Policy!));
        Assert.False(pcObserved);
    }

    // "Pasta da turma": a phone's agent holds the lesson as a PC Agent does,
    // so a phone that was off when the lesson started gets it when it is on.
    [Fact]
    public async Task ClassLesson_IsHandedToAPhoneToo()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "aula do 3º A")));
        var lesson = new ClassLesson(1000, Guid.NewGuid(), "3º A", 1, Ended: false);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint, Kind = DeviceKind.Android },
            session.MasterCertificate, session.Port, agent.Port, classLesson: () => lesson);
        var polls = new Polls(poller);

        poller.Start();

        await WaitUntil(() => agent.LessonsReceived.Length > 0);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L }, agent.LessonsReceived);
    }
}
