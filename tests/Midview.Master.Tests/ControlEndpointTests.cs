using Midview.Master;
using Xunit;

public class ControlEndpointTests
{
    [Fact]
    public void A_paired_machine_with_a_reachable_helper_chooses_5902_and_the_agent_thumbprint()
    {
        var machine = new KnownMachine("Aluno-1", "10.0.0.5", "session-thumb", "agent-thumb");

        var choice = ControlChoice.Choose(machine, helperReachable: true);

        Assert.Equal(ControlEndpoint.Helper, choice.Endpoint);
        Assert.Equal(5902, choice.Port);
        Assert.Equal("agent-thumb", choice.Thumbprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_agent_thumbprint_chooses_the_session_endpoint(string? agentThumbprint)
    {
        var machine = new KnownMachine("Aluno-1", "10.0.0.5", "session-thumb", agentThumbprint);

        foreach (var reachable in new[] { true, false })
        {
            var choice = ControlChoice.Choose(machine, reachable);

            Assert.Equal(ControlEndpoint.Session, choice.Endpoint);
            Assert.Equal(5901, choice.Port);
            Assert.Equal("session-thumb", choice.Thumbprint);
        }
    }

    [Fact]
    public void An_unreachable_helper_falls_back_to_the_session()
    {
        var machine = new KnownMachine("Aluno-1", "10.0.0.5", "session-thumb", "agent-thumb");

        var choice = ControlChoice.Choose(machine, helperReachable: false);

        Assert.Equal(ControlEndpoint.Session, choice.Endpoint);
        Assert.Equal(5901, choice.Port);
        Assert.Equal("session-thumb", choice.Thumbprint);
    }
}
