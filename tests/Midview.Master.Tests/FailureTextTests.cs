using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class FailureTextTests
{
    public static TheoryData<Exception, string> UsualCases => new()
    {
        { new SocketException((int)SocketError.ConnectionRefused), "Sem usuário logado" },
        { new SocketException((int)SocketError.HostNotFound), "Nome não encontrado na rede" },
        { new SocketException((int)SocketError.TimedOut), "Offline" },
        // What a connect that hit the 5 s timeout throws: "The operation was canceled."
        { new OperationCanceledException(), "Offline" },
        { new AuthenticationException("The remote certificate was rejected"), "Certificado recusado" },
        { new IOException("Unable to read data from the transport connection", new SocketException((int)SocketError.ConnectionReset)), "Sem resposta" },
        { new EndOfStreamException("Connection closed while reading message"), "Sem resposta" },
    };

    [Theory]
    [MemberData(nameof(UsualCases))]
    public void UsualCases_UseTheGridsWords(Exception ex, string expected)
    {
        Assert.Equal(expected, FailureText.Describe(ex));
        Assert.Equal(expected, FailureText.Status(ex));
    }

    [Fact]
    public void RefusedByTheAgent_MeansItIsNotRunning()
    {
        Assert.Equal("O Midview não está rodando nessa máquina",
            FailureText.DescribeAgent(new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal("Offline", FailureText.DescribeAgent(new OperationCanceledException()));
    }

    [Fact]
    public void SomethingUnexpected_KeepsTheTechnicalMessage_ButTheTileStaysShort()
    {
        var ex = new InvalidDataException("resposta inesperada", new FormatException("byte 7"));

        Assert.Equal("resposta inesperada -> byte 7", FailureText.Describe(ex));
        Assert.Equal("Sem resposta", FailureText.Status(ex));
    }

    [Fact]
    public async Task ARealMessageToNobody_ReadsAsTheGridWouldShowIt()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop(); // nothing listens there now
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() => CommandClient.SendCommand(
            "127.0.0.1", port, "ANY", masterCertificate, MessageType.ShowMessage, TextMessage.Encode("Olá")))
            .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("Sem usuário logado", FailureText.Describe(failure));
    }
}
