using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Mensagem" and "Bate-papo" with phones: a phone's agent (port 28900) takes
// the PC's messages, answers a ChatOpen before the conversation, and an
// older agent is named as such.
public class PhoneTalkTests
{
    private static readonly TimeSpan Eventually = TimeSpan.FromSeconds(30);

    private static KnownMachine Phone(string name = "Celular 01", string? agent = "AB12") =>
        new(name, "192.0.2.9", "ANY", agent, Kind: DeviceKind.Android);

    private static KnownMachine Pc(string name = "ALUNO-01") => new(name, "192.0.2.1", "ANY", "AGENT");

    [Fact]
    public void A_phone_that_shows_the_message_is_a_success()
    {
        var result = MessageTargets.FromPhone(Phone(), PhoneAgentCommand.Send(Phone(), MessageTargets.OldAgent, thumbprint =>
        {
            Assert.Equal("AB12", thumbprint);
            return (true, "mensagem mostrada");
        }));
        Assert.Equal(new MessageTargets.Result("Celular 01", null), result);
    }

    // Under the teacher's lock screen the phone keeps it for later, and says so.
    [Fact]
    public void A_phone_that_waits_for_the_lock_screen_says_so()
    {
        var result = MessageTargets.FromPhone(Phone(), (true, "a tela está bloqueada: a mensagem aparece quando ela for liberada"));
        Assert.Null(result.Failure);
        Assert.Equal("a tela está bloqueada: a mensagem aparece quando ela for liberada", result.Note);
    }

    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public void An_older_phone_agent_does_not_show_messages(string answer)
    {
        var result = MessageTargets.FromPhone(Phone(), PhoneAgentCommand.Send(Phone(), MessageTargets.OldAgent, _ => (false, answer)));
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não mostra mensagens: atualize-o", result.Failure);
    }

    [Fact]
    public void A_phone_without_its_identity_is_not_asked()
    {
        var asked = false;
        var result = MessageTargets.FromPhone(Phone(agent: null),
            PhoneAgentCommand.Send(Phone(agent: null), MessageTargets.OldAgent, _ => { asked = true; return (true, ""); }));
        Assert.False(asked);
        Assert.Equal(PhoneAgentCommand.NoIdentity, result.Failure);
    }

    [Fact]
    public void A_phone_that_does_not_answer_is_offline() =>
        Assert.Equal(FailureText.Offline, MessageTargets.FromPhone(Phone(),
            PhoneAgentCommand.Send(Phone(), MessageTargets.OldAgent, _ => throw new SocketException((int)SocketError.TimedOut))).Failure);

    [Fact]
    public void The_summary_lists_failures_and_what_the_phones_said()
    {
        Assert.Equal("Mensagem enviada.", MessageTargets.Summary(new[] { new MessageTargets.Result("ALUNO-01", null) }));
        Assert.Equal("Mensagem enviada para as 2 máquinas.", MessageTargets.Summary(new[]
        {
            new MessageTargets.Result("ALUNO-01", null), new MessageTargets.Result("Celular 01", null),
        }));
        Assert.Equal(
            "Enviada para 2 de 3. Não chegou em:\n• ALUNO-02: Offline\nObservações:\n• Celular 01: a tela está bloqueada: a mensagem aparece quando ela for liberada",
            MessageTargets.Summary(new[]
            {
                new MessageTargets.Result("ALUNO-01", null),
                new MessageTargets.Result("ALUNO-02", "Offline"),
                new MessageTargets.Result("Celular 01", null, "a tela está bloqueada: a mensagem aparece quando ela for liberada"),
            }));
    }

    [Fact]
    public void A_phones_yes_to_the_chat_has_nothing_to_tell()
    {
        Assert.Null(ChatConnection.PhoneAnswer(Ack(true, "conversa aberta")));
        Assert.Equal("a tela está bloqueada: a conversa aparece quando ela for liberada",
            ChatConnection.PhoneAnswer(Ack(true, "a tela está bloqueada: a conversa aparece quando ela for liberada")));
    }

    [Theory]
    [InlineData("Ainda não funciona em celular.", "o Midview deste celular é de uma versão anterior e ainda não conversa: atualize-o")]
    [InlineData("", "o Midview deste celular é de uma versão anterior e ainda não conversa: atualize-o")]
    [InlineData("a conversa não pôde ser aberta", "a conversa não pôde ser aberta")]
    public void A_phones_no_to_the_chat_says_why(string answer, string expected)
    {
        var refused = Assert.Throws<ChatRefusedException>(() => ChatConnection.PhoneAnswer(Ack(false, answer)));
        Assert.Equal(expected, refused.Message);
        Assert.Equal(expected, ChatConnection.DescribeFailure(Phone(), refused));
    }

    [Fact]
    public void A_failure_to_reach_a_phone_names_its_agent()
    {
        var refusedConnection = new SocketException((int)SocketError.ConnectionRefused);
        Assert.Equal("O Midview não está rodando neste celular", ChatConnection.DescribeFailure(Phone(), refusedConnection));
        Assert.Equal("Sem usuário logado", ChatConnection.DescribeFailure(Pc(), refusedConnection));
    }

    [Fact]
    public void A_phone_without_its_identity_cannot_chat()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        var refused = Assert.Throws<ChatRefusedException>(() => ChatConnection.Open(Phone(agent: null), master));
        Assert.Equal(PhoneAgentCommand.NoIdentity, refused.Message);
    }

    // A fake phone agent: it checks the teacher's certificate, takes the
    // ChatOpen, says yes, and then the lines flow both ways as with a PC.
    [Fact]
    public async Task A_chat_with_a_phone_waits_for_its_yes_then_flows_both_ways()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var phoneCertificate = SelfSignedCertificate.Create("phone-under-test");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var teacherLine = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var studentLine = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var phone = OwnThread(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            using var channel = SecureChannel.AuthenticateAsServer(
                tcpClient, phoneCertificate, new TrustStore(new[] { master.Thumbprint }));
            Assert.Equal(MessageType.ChatOpen, channel.Receive().Type);
            channel.Send(Ack(true, "a tela está bloqueada: a conversa aparece quando ela for liberada"));
            while (true)
            {
                var message = channel.Receive();
                if (message.Type == MessageType.ChatText)
                {
                    teacherLine.TrySetResult(TextMessage.Decode(message.Payload)!);
                    channel.Send(new Message(MessageType.ChatText, TextMessage.Encode("terminei")));
                    return;
                }
            }
        });
        try
        {
            var machine = new KnownMachine(
                "Celular 01", "127.0.0.1", phoneCertificate.Thumbprint, phoneCertificate.Thumbprint, Kind: DeviceKind.Android);
            using var chat = await OwnThread(() => ChatConnection.Open(machine, master, port)).WaitAsync(Eventually);
            Assert.Equal("a tela está bloqueada: a conversa aparece quando ela for liberada", chat.Notice);
            chat.StudentMessageReceived += text => studentLine.TrySetResult(text);
            chat.Start();

            Assert.True(chat.Send("Terminou?"));
            Assert.Equal("Terminou?", await teacherLine.Task.WaitAsync(Eventually));
            Assert.Equal("terminei", await studentLine.Task.WaitAsync(Eventually));
            await phone.WaitAsync(Eventually);
        }
        finally
        {
            listener.Stop();
        }
    }

    // An older agent answers the ChatOpen with "Ainda não funciona em celular.".
    [Fact]
    public async Task An_older_phone_agent_refuses_the_chat()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var phoneCertificate = SelfSignedCertificate.Create("phone-under-test");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var phone = OwnThread(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            using var channel = SecureChannel.AuthenticateAsServer(
                tcpClient, phoneCertificate, new TrustStore(new[] { master.Thumbprint }));
            channel.Receive();
            channel.Send(Ack(false, "Ainda não funciona em celular."));
        });
        try
        {
            var machine = new KnownMachine(
                "Celular 01", "127.0.0.1", phoneCertificate.Thumbprint, phoneCertificate.Thumbprint, Kind: DeviceKind.Android);
            var refused = await Assert.ThrowsAsync<ChatRefusedException>(
                () => OwnThread(() => ChatConnection.Open(machine, master, port)).WaitAsync(Eventually));
            Assert.Equal(ChatConnection.OldAgent, refused.Message);
            await phone.WaitAsync(Eventually);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Message Ack(bool succeeded, string detail) =>
        new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));

    private static Task OwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static Task<T> OwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
