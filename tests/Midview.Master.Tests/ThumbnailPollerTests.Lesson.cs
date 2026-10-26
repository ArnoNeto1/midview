using Midview.Master;
using Midview.Protocol;
using Xunit;

// ThumbnailPoller handing the class folder's lesson to the Agent (Ruling 23):
// an Agent busy with its student's folders took nothing and is asked again in
// a moment (BusyRetryInterval); any other no waits the usual retry interval.
public partial class ThumbnailPollerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALessonTheAgentRefuses_IsTriedAgainSoon_OnlyWhenItWasBusy(bool busy)
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(
            session.MasterCertificate,
            new Message(MessageType.CommandAck, CommandResult.Encode(false, busy ? ClassHomeClaim.BusyText : "não foi possível gravar a aula")));
        var lesson = new ClassLesson(1000, Guid.NewGuid(), "3º A", 1, Ended: false);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            classLesson: () => lesson)
        {
            BusyRetryInterval = TimeSpan.Zero,
        };
        var thumbnails = 0;
        poller.ThumbnailReceived += _ => Interlocked.Increment(ref thumbnails);

        poller.Start();

        if (busy)
        {
            await WaitUntil(() => agent.LessonsReceived.Length >= 2);
            Assert.All(agent.LessonsReceived, savedAt => Assert.Equal(1000L, savedAt));
        }
        else
        {
            await WaitUntil(() => Volatile.Read(ref thumbnails) >= 4);
            Assert.Equal(new[] { 1000L }, agent.LessonsReceived);
        }
    }
}
