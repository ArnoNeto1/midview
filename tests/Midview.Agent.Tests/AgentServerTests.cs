using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class AgentServerTests
{
    [Fact]
    public async Task LabConfig_AppliesReadsBackAndResets_ThroughTheAgentServer()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");

        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-labcfg-{Guid.NewGuid():N}.json");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewallController());
        var writer = new LabPolicyWriter(Registry.CurrentUser.CreateSubKey(rootPath, writable: true));
        var labConfig = new LabConfigCommandHandler(statePath, writer, new AppBlockEnforcer(new EmptyInspector()));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, labConfig: labConfig);

        Message Exchange(Message request)
        {
            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
            channel.Send(request);
            return channel.Receive();
        }

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var set = await Task.Run(() => Exchange(new Message(
                MessageType.SetLabConfig, (LabConfig.Off(1000) with { Lockdown = true, BlockDownloads = true }).Encode())))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MessageType.CommandAck, set.Type);
            Assert.True(CommandResult.Decode(set.Payload).Succeeded);

            var status = await Task.Run(() => Exchange(new Message(MessageType.GetLabConfig, Array.Empty<byte>())))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.LabConfigStatus, status.Type);
            Assert.True(LabConfig.TryDecode(status.Payload, out var applied));
            Assert.True(applied.Lockdown);
            Assert.True(applied.BlockDownloads);
            using (var chrome = root.OpenSubKey(@"Policies\Google\Chrome"))
            {
                Assert.Equal(3, chrome!.GetValue("DownloadRestriction"));
            }

            var reset = await Task.Run(() => Exchange(new Message(MessageType.ResetLabConfig, Array.Empty<byte>())))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(CommandResult.Decode(reset.Payload).Succeeded);
            Assert.Null(root.OpenSubKey(@"Policies\Google\Chrome"));
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            try { Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task UninstallAndClearDownloads_WorkThroughTheAgentServer()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");

        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
        using (var app = root.CreateSubKey(@"Microsoft\Windows\CurrentVersion\Uninstall\AppQuiet", writable: true))
        {
            app.SetValue("DisplayName", "App Quiet");
            app.SetValue("QuietUninstallString", "un.exe /S");
        }
        var ranCommands = new List<string>();
        var uninstaller = new ProgramUninstaller(
            Registry.CurrentUser.CreateSubKey(rootPath, writable: true), cmd => { ranCommands.Add(cmd); return 0; });

        var usersRoot = Path.Combine(Path.GetTempPath(), $"midview-users-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(usersRoot, "aluno", "Downloads"));
        File.WriteAllText(Path.Combine(usersRoot, "aluno", "Downloads", "x.bin"), "x");
        var labStatePath = Path.Combine(Path.GetTempPath(), $"midview-labcfg-{Guid.NewGuid():N}.json");
        var pendingPath = Path.Combine(Path.GetTempPath(), $"midview-pending-{Guid.NewGuid():N}.json");
        var pending = new PendingUninstalls(pendingPath);
        var labConfig = new LabConfigCommandHandler(
            labStatePath, new LabPolicyWriter(Registry.CurrentUser.CreateSubKey(rootPath + @"\lab", writable: true)),
            new AppBlockEnforcer(new EmptyInspector()), usersRoot);

        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock,
            labConfig: labConfig, uninstaller: uninstaller, pendingUninstalls: pending);

        Message Exchange(Message request)
        {
            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
            channel.Send(request);
            return channel.Receive();
        }

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var list = await Task.Run(() => Exchange(new Message(MessageType.GetInstalledPrograms, Array.Empty<byte>())))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MessageType.InstalledPrograms, list.Type);
            Assert.True(ProgramList.TryDecode(list.Payload, out var programs));
            Assert.Contains("App Quiet", programs);

            var uninstall = await Task.Run(() => Exchange(new Message(MessageType.UninstallPrograms, ProgramList.Encode(new[] { "App Quiet" }))))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(CommandResult.Decode(uninstall.Payload).Succeeded);
            Assert.Equal(new[] { "un.exe /S" }, ranCommands);
            // Kept for the students who weren't logged on.
            Assert.Equal(new[] { "App Quiet" }, pending.Current());

            // A missing scope byte is refused, not treated as "delete all".
            var bad = await Task.Run(() => Exchange(new Message(MessageType.ClearDownloads, Array.Empty<byte>())))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(CommandResult.Decode(bad.Payload).Succeeded);
            Assert.NotEmpty(Directory.GetFileSystemEntries(Path.Combine(usersRoot, "aluno", "Downloads")));

            var clear = await Task.Run(() => Exchange(new Message(MessageType.ClearDownloads, new[] { (byte)DownloadsScope.All })))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(CommandResult.Decode(clear.Payload).Succeeded);
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(usersRoot, "aluno", "Downloads")));
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            try { Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            try { File.Delete(labStatePath); } catch { }
            try { File.Delete(pendingPath); } catch { }
            try { Directory.Delete(usersRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SetThenGetClassLesson_RoundTrips()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-lesson-{Guid.NewGuid():N}.json");
        var handler = new ClassLessonHandler(
            statePath, () => null, _ => null, new AppBlockEnforcer(new EmptyInspector()),
            System.Security.Principal.WindowsIdentity.GetCurrent().User!, () => DateTime.Now);
        var lesson = new ClassLesson(100, Guid.NewGuid(), "3º A", DateTimeOffset.Now.ToUnixTimeSeconds(), Ended: false);
        try
        {
            var answers = await ExchangeWithAgent(handler,
                new Message(MessageType.SetClassLesson, lesson.Encode()),
                new Message(MessageType.GetClassLesson, Array.Empty<byte>()));

            Assert.Equal(MessageType.CommandAck, answers[0].Type);
            var set = CommandResult.Decode(answers[0].Payload);
            Assert.True(set.Succeeded);
            Assert.Equal("aula do 3º A", set.Detail);
            Assert.Equal(MessageType.ClassLessonStatus, answers[1].Type);
            Assert.True(ClassLesson.TryDecode(answers[1].Payload, out var current));
            Assert.Equal(lesson, current);
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    // What an Agent from before the class folder answers, and what the
    // Master shows as "Midview antigo".
    [Fact]
    public async Task WithoutHandler_GetClassLesson_AnswersCommandAck()
    {
        var answer = (await ExchangeWithAgent(null, new Message(MessageType.GetClassLesson, Array.Empty<byte>())))[0];

        Assert.Equal(MessageType.CommandAck, answer.Type);
        Assert.Equal(new byte[] { 0 }, answer.Payload);
    }

    [Fact]
    public async Task WithoutHandler_SetClassLesson_AnswersCommandAck()
    {
        var lesson = new ClassLesson(100, Guid.NewGuid(), "3º A", DateTimeOffset.Now.ToUnixTimeSeconds(), Ended: false);

        var answer = (await ExchangeWithAgent(null, new Message(MessageType.SetClassLesson, lesson.Encode())))[0];

        Assert.Equal(MessageType.CommandAck, answer.Type);
        Assert.Equal(new byte[] { 0 }, answer.Payload);
    }

    // One connection per request, in order, against an AgentServer with only
    // the class lesson handler (or none) besides site blocking.
    private static async Task<Message[]> ExchangeWithAgent(ClassLessonHandler? classLesson, params Message[] requests)
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, classLesson: classLesson);

        Message Exchange(Message request)
        {
            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
            channel.Send(request);
            return channel.Receive();
        }

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var answers = new List<Message>();
            foreach (var request in requests)
            {
                answers.Add(await Task.Run(() => Exchange(request)).WaitAsync(TimeSpan.FromSeconds(30)));
            }
            return answers.ToArray();
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class EmptyInspector : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> ListInteractive() => Array.Empty<RunningProcess>();
        public ProcessFacts? Inspect(RunningProcess process) => null;
        public bool End(RunningProcess process) => true;
        public void Notify(int sessionId, string message) { }
    }

    [Fact]
    public async Task ConnectionThatNeverHandshakes_DoesNotBlockTheNextCommand()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
        var firewall = new FakeFirewallController();
        var handler = new SiteBlockCommandHandler(new SiteBlockState(), statePath, "unused-hosts-path", firewall);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), handler);

        Message SendCommand(MessageType type)
        {
            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
            channel.Send(new Message(type, Array.Empty<byte>()));
            return channel.Receive();
        }

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            // The process's first TLS handshake pays for loading SChannel and
            // the keys (seconds on a busy CI runner), which the 5 s below is
            // not about -- so one command goes through before the stall.
            var warmUp = await Task.Run(() => SendCommand(MessageType.UnblockInternet)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MessageType.CommandAck, warmUp.Type);

            // Opens a TCP connection and then sends nothing at all.
            using var stalledClient = new TcpClient();
            stalledClient.Connect(IPAddress.Loopback, port);

            // Well under the server's own 10 s handshake timeout, so this can
            // only pass if the stalled connection isn't in the way at all.
            var ack = await Task.Run(() => SendCommand(MessageType.BlockInternet)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.CommandAck, ack.Type);
            Assert.Equal(new byte[] { 1 }, ack.Payload);
            Assert.Equal(1, firewall.ApplyBlockCalls);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(statePath);
        }
    }
}
