using System.Text;
using System.Text.Json;
using Midview.Protocol;
using Xunit;

public class DiscoveryTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string Agent = new('B', 40);
    private static readonly Guid Probe = Guid.Parse("6f2b1c7e-4d3a-4b8e-9c61-2e7a5d0b9f14");

    [Fact]
    public void AProbe_RoundTrips_InTheSpecsShape()
    {
        var bytes = Discovery.Encode(new DiscoveryProbe(Probe, Master));

        Assert.Equal(new DiscoveryProbe(Probe, Master), Discovery.ParseProbe(bytes));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(1, json.RootElement.GetProperty("midview").GetInt32());
        Assert.Equal(Probe.ToString(), json.RootElement.GetProperty("probe").GetString());
        Assert.Equal(Master, json.RootElement.GetProperty("master").GetString());
    }

    [Fact]
    public void AReply_RoundTrips_InTheSpecsShape()
    {
        var reply = new DiscoveryReply(Probe, "ALUNO-01", Agent, TeacherStatus.Others, true);
        var bytes = Discovery.Encode(reply);

        Assert.Equal(reply, Discovery.ParseReply(bytes));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(1, json.RootElement.GetProperty("midview").GetInt32());
        Assert.Equal("ALUNO-01", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(Agent, json.RootElement.GetProperty("agent").GetString());
        Assert.Equal("others", json.RootElement.GetProperty("teachers").GetString());
        Assert.True(json.RootElement.GetProperty("newTeacherOpen").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"midview\":2,\"probe\":\"6f2b1c7e-4d3a-4b8e-9c61-2e7a5d0b9f14\",\"master\":\"AAAA\"}")]
    [InlineData("{\"midview\":1,\"probe\":\"6f2b1c7e-4d3a-4b8e-9c61-2e7a5d0b9f14\"}")]
    [InlineData("{\"midview\":1,\"master\":\"AAAA\"}")]
    [InlineData("{\"midview\":1,\"probe\":\"nada\",\"master\":\"AAAA\"}")]
    public void WhatIsNotAProbe_IsIgnored(string packet)
    {
        Assert.Null(Discovery.ParseProbe(Encoding.UTF8.GetBytes(packet)));
    }

    [Fact]
    public void AReplyIsNotAProbe_NorTheOtherWayRound()
    {
        Assert.Null(Discovery.ParseProbe(Discovery.Encode(new DiscoveryReply(Probe, "ALUNO-01", Agent, TeacherStatus.None, false))));
        Assert.Null(Discovery.ParseReply(Discovery.Encode(new DiscoveryProbe(Probe, Master))));
    }

    [Fact]
    public void AReply_WithAnUnknownStatus_OrNoName_IsIgnored()
    {
        Assert.Null(Discovery.ParseReply(Discovery.Encode(new DiscoveryReply(Probe, "ALUNO-01", Agent, "maybe", false))));
        Assert.Null(Discovery.ParseReply(Discovery.Encode(new DiscoveryReply(Probe, "", Agent, TeacherStatus.None, false))));
    }

    [Fact]
    public void APacketOverTheLimit_IsIgnored()
    {
        var big = Discovery.Encode(new DiscoveryReply(Probe, new string('x', Discovery.MaxPacketSize), Agent, TeacherStatus.None, false));

        Assert.True(big.Length > Discovery.MaxPacketSize);
        Assert.Null(Discovery.ParseReply(big));
    }
}
