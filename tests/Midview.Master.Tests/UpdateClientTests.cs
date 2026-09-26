using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class UpdateClientTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly X509Certificate2 _masterCertificate = SelfSignedCertificate.Create("master-under-test");
    private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("agent-under-test");
    private readonly string _package = Path.Combine(Path.GetTempPath(), $"midview-update-client-{Guid.NewGuid():N}");
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"midview-update-client-temp-{Guid.NewGuid():N}");

    public UpdateClientTests()
    {
        var folder = UpdatePackage.StudentProgramFolder(_package);
        Directory.CreateDirectory(folder);
        var files = new[] { "Midview.Agent.exe", "Midview.Session.exe" };
        foreach (var file in files)
        {
            // Big enough to take several chunks.
            File.WriteAllBytes(Path.Combine(folder, file), RandomNumberGenerator.GetBytes(FileTransfer.ChunkSize * 2 + 123));
        }
        File.WriteAllText(Path.Combine(folder, NetworkUpdate.ManifestFileName), JsonSerializer.Serialize(new
        {
            build = "2026-09-26 19:40 a1b2c3d",
            updateProtocol = NetworkUpdate.ProtocolVersion,
            files,
        }));
    }

    public void Dispose()
    {
        _masterCertificate.Dispose();
        _agentCertificate.Dispose();
        Directory.Delete(_package, recursive: true);
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private KnownMachine Machine => new("Aluno-1", "127.0.0.1", "SESSION", _agentCertificate.Thumbprint);

    [Fact]
    public async Task GetStatus_SendsTheAttemptId_AndReadsTheReport()
    {
        var attemptId = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);
        var report = new UpdateStatusReport("AB12", "2026-09-26 19:40 a1b2c3d", true,
            new UpdateAttemptReport(Convert.ToHexString(attemptId), UpdateStates.Committed, "atualizada", DateTimeOffset.UnixEpoch));
        byte[]? asked = null;
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            var request = channel.Receive();
            Assert.Equal(MessageType.GetUpdateStatus, request.Type);
            asked = request.Payload;
            channel.Send(new Message(MessageType.UpdateStatus, report.Encode()));
        });

        var status = await Task.Run(() => UpdateClient.GetStatus(Machine, _masterCertificate, attemptId, agent.Port)).WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Equal(attemptId, asked);
        Assert.False(status.OldAgent);
        Assert.Equal(report, status.Report);
    }

    [Fact]
    public async Task GetStatus_WithoutAttempt_SendsNothing()
    {
        byte[]? asked = null;
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            asked = channel.Receive().Payload;
            channel.Send(new Message(MessageType.UpdateStatus, new UpdateStatusReport(null, null, false, null).Encode()));
        });

        var status = await Task.Run(() => UpdateClient.GetStatus(Machine, _masterCertificate, port: agent.Port)).WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Empty(asked!);
        Assert.False(status.Report!.Ready);
    }

    [Fact]
    public async Task GetStatus_FromAnAgentBeforeNetworkUpdates_IsOldAgent()
    {
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            // What an older Agent answers to a command it doesn't know.
            channel.Send(new Message(MessageType.CommandAck, new byte[] { 0 }));
        });

        var status = await Task.Run(() => UpdateClient.GetStatus(Machine, _masterCertificate, port: agent.Port)).WaitAsync(Timeout);

        Assert.True(status.OldAgent);
        Assert.Null(status.Report);
    }

    [Fact]
    public async Task Push_SendsTheOfferAndTheWholeZip_AndReportsReceived()
    {
        using var package = UpdatePackage.Create(_package, _temp);
        var attemptId = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);
        UpdateOffer? offered = null;
        var received = new MemoryStream();
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            var offer = channel.Receive();
            Assert.Equal(MessageType.UpdateOffer, offer.Type);
            Assert.True(NetworkUpdate.TryDecodeOffer(offer.Payload, out var decoded));
            offered = decoded;
            channel.Send(new Message(MessageType.UpdateAccepted, Array.Empty<byte>()));
            while (received.Length < decoded.ZipSize)
            {
                var chunk = channel.Receive();
                Assert.Equal(MessageType.FileChunk, chunk.Type);
                received.Write(chunk.Payload);
            }
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "recebida")));
        });
        var progress = new List<(long Sent, long Total)>();

        var result = await Task.Run(() => UpdateClient.Push(
            Machine, _masterCertificate, package, attemptId, (sent, total) => progress.Add((sent, total)), port: agent.Port))
            .WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Equal(new PushResult(PushOutcome.Received, "recebida"), result);
        Assert.Equal(attemptId, offered!.AttemptId);
        Assert.Equal(package.Fingerprint, offered.Fingerprint);
        Assert.Equal(File.ReadAllBytes(package.ZipPath), received.ToArray());
        Assert.Equal((package.ZipSize, package.ZipSize), progress[^1]);
    }

    [Theory]
    [InlineData(true, "já está nesta versão", PushOutcome.AlreadyInstalled)]
    [InlineData(false, "a máquina vai desligar em breve; atualize depois", PushOutcome.Refused)]
    [InlineData(false, "", PushOutcome.OldAgent)]
    public async Task Push_AnsweredRightAway_SendsNoZip(bool succeeded, string detail, PushOutcome expected)
    {
        using var package = UpdatePackage.Create(_package, _temp);
        var extra = false;
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            try
            {
                channel.Receive();
                extra = true;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException)
            {
                // The Master hung up, as it should.
            }
        });

        var result = await Task.Run(() => UpdateClient.Push(
            Machine, _masterCertificate, package, new byte[NetworkUpdate.AttemptIdLength], port: agent.Port))
            .WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(detail, result.Detail);
        Assert.False(extra);
    }

    [Fact]
    public async Task Push_TheAgentRefusingAfterTheZip_IsRefused()
    {
        using var package = UpdatePackage.Create(_package, _temp);
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.UpdateAccepted, Array.Empty<byte>()));
            DrainZip(channel, package.ZipSize);
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, "o pacote chegou corrompido (o hash não confere)")));
        });

        var result = await Task.Run(() => UpdateClient.Push(
            Machine, _masterCertificate, package, new byte[NetworkUpdate.AttemptIdLength], port: agent.Port))
            .WaitAsync(Timeout);

        Assert.Equal(new PushResult(PushOutcome.Refused, "o pacote chegou corrompido (o hash não confere)"), result);
    }

    [Fact]
    public async Task Push_NoAnswerAfterTheZip_IsSentNoAnswer()
    {
        using var package = UpdatePackage.Create(_package, _temp);
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.UpdateAccepted, Array.Empty<byte>()));
            DrainZip(channel, package.ZipSize);
            // Hangs up without answering (the Agent restarting, say).
        });

        var result = await Task.Run(() => UpdateClient.Push(
            Machine, _masterCertificate, package, new byte[NetworkUpdate.AttemptIdLength], port: agent.Port))
            .WaitAsync(Timeout);

        Assert.Equal(PushOutcome.SentNoAnswer, result.Outcome);
    }

    [Fact]
    public async Task Push_Cancelled_StopsSending()
    {
        using var package = UpdatePackage.Create(_package, _temp);
        using var cancel = new CancellationTokenSource();
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.UpdateAccepted, Array.Empty<byte>()));
            try
            {
                DrainZip(channel, package.ZipSize);
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException)
            {
                // The Master hung up halfway, as it should.
            }
        });

        var push = Task.Run(() => UpdateClient.Push(
            Machine, _masterCertificate, package, new byte[NetworkUpdate.AttemptIdLength],
            (sent, _) => cancel.Cancel(), cancel.Token, agent.Port));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => push.WaitAsync(Timeout));
    }

    [Fact]
    public void Push_WithoutTheAgentsIdentity_IsRefusedBeforeConnecting()
    {
        using var package = UpdatePackage.Create(_package, _temp);
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, _ => { });

        Assert.Throws<InvalidOperationException>(() => UpdateClient.Push(
            Machine with { AgentThumbprint = null }, _masterCertificate, package, new byte[NetworkUpdate.AttemptIdLength],
            port: agent.Port));
    }

    [Fact]
    public async Task Push_ToASessionCertificate_FailsTheHandshake()
    {
        using var package = UpdatePackage.Create(_package, _temp);
        using var impostor = SelfSignedCertificate.Create("not-the-agent");
        using var agent = new ScriptedAgent(impostor, _masterCertificate, channel => channel.Receive());

        var push = Task.Run(() => UpdateClient.Push(
            Machine, _masterCertificate, package, new byte[NetworkUpdate.AttemptIdLength], port: agent.Port));

        await Assert.ThrowsAnyAsync<Exception>(() => push.WaitAsync(Timeout));
    }

    private static void DrainZip(SecureChannel channel, long zipSize)
    {
        long received = 0;
        while (received < zipSize)
        {
            received += channel.Receive().Payload.Length;
        }
    }

    // One connection on loopback, answered by script as an Agent would.
    private sealed class ScriptedAgent : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public ScriptedAgent(X509Certificate2 agentCertificate, X509Certificate2 masterCertificate, Action<SecureChannel> script)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Done = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync();
                client.ReceiveTimeout = 20_000;
                client.SendTimeout = 20_000;
                using var channel = SecureChannel.AuthenticateAsServer(
                    client, agentCertificate, new TrustStore(new[] { masterCertificate.Thumbprint }));
                script(channel);
            });
        }

        public int Port { get; }

        public Task Done { get; }

        public void Dispose() => _listener.Stop();
    }
}
