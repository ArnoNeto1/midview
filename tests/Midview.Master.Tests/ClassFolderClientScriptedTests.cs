using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives ClassFolderClient against scripted stand-ins for a Session: one from
// before the class folder (it answers with a screen tile, as it would to any
// message it doesn't know) and one a student rewrote (a file longer or shorter
// than its header says, an answer out of turn). Never a lab machine.
public sealed class ClassFolderClientScriptedTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    // The Master's side blocks on the network, on a thread of its own: a pool
    // thread held meanwhile is one its connect may be waiting for, and a busy
    // test run's pool can be slow to give one.
    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);


    // ---- an old Session, and a hostile one ----

    [Fact]
    public async Task List_OldSession_IsFlagged()
    {
        using var session = new Scripted(channel => channel.Send(new Message(MessageType.FrameTile, new byte[] { 1, 2, 3 })));

        var listing = await Run(() => ClassFolderClient.List(session.Machine, session.MasterCertificate, session.Port));

        Assert.True(listing.OldSession);
        Assert.Null(listing.Problem);
        Assert.Empty(listing.Entries);
        Assert.Equal(MessageType.ListClassFolder, session.Request!.Type);
    }

    [Fact]
    public async Task EveryOtherMessage_ToAnOldSession_SaysMidviewAntigo()
    {
        var calls = new Func<Scripted, (bool Succeeded, string Detail)>[]
        {
            s => ClassFolderClient.Pull(s.Machine, s.MasterCertificate, Guid.NewGuid(), "a.txt", new MemoryStream(), s.Port),
            s => ClassFolderClient.Delete(s.Machine, s.MasterCertificate, Guid.NewGuid(), "a.txt", s.Port),
            s => ClassFolderClient.Rename(s.Machine, s.MasterCertificate, Guid.NewGuid(), "a.txt", "b.txt", s.Port),
            s => ClassFolderClient.ClearSetAside(s.Machine, s.MasterCertificate, Guid.NewGuid(), Array.Empty<ClassFolderEntry>(), s.Port),
            s => ClassFolderClient.Push(s.Machine, s.MasterCertificate, Guid.NewGuid(), "a.txt", s.TempFile(1), DateTime.UtcNow.Ticks, null, s.Port),
        };

        foreach (var call in calls)
        {
            using var session = new Scripted(channel => channel.Send(new Message(MessageType.FrameTile, new byte[] { 1 })));
            var (succeeded, detail) = await Run(() => call(session));
            Assert.False(succeeded);
            Assert.Equal(CommandClient.OlderSession, detail);
        }
    }

    [Fact]
    public async Task List_AnythingElse_IsAnError()
    {
        using var garbled = new Scripted(channel => channel.Send(new Message(MessageType.ClassFolderListing, new byte[] { 9, 9 })));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Run(() => ClassFolderClient.List(garbled.Machine, garbled.MasterCertificate, garbled.Port)));

        using var other = new Scripted(channel => channel.Send(new Message(MessageType.ShowMessage, TextMessage.Encode("oi"))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Run(() => ClassFolderClient.List(other.Machine, other.MasterCertificate, other.Port)));
    }

    private static readonly Guid PartsLesson = Guid.NewGuid();

    private static Message ListingPart(Guid lessonId, bool more, params string[] paths) =>
        new(MessageType.ClassFolderListing, ClassFolderWire.EncodeListing(
            lessonId, null, paths.Select(path => new ClassFolderEntry(lessonId, path, 1, 1)).ToArray(), more));

    [Fact]
    public async Task List_InParts_IsReadToTheLast()
    {
        using var session = new Scripted(channel =>
        {
            channel.Send(ListingPart(PartsLesson, more: true, "a.txt", "b.txt"));
            channel.Send(ListingPart(PartsLesson, more: true));
            channel.Send(ListingPart(PartsLesson, more: false, "c.txt"));
        });

        var listing = await Run(() => ClassFolderClient.List(session.Machine, session.MasterCertificate, session.Port));

        Assert.Equal(PartsLesson, listing.CurrentLessonId);
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, listing.Entries.Select(entry => entry.Path));
    }

    // A listing that stops before its last part is never taken for the whole: the round would plan on it.
    [Fact]
    public async Task List_ThatStopsBeforeItsLastPart_IsRefused()
    {
        using var session = new Scripted(channel => channel.Send(ListingPart(PartsLesson, more: true, "a.txt")));

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Run(() => ClassFolderClient.List(session.Machine, session.MasterCertificate, session.Port)));
        Assert.Contains("incompleta", refused.Message);
    }

    [Fact]
    public async Task List_APartThatDoesNotBelong_IsRefused()
    {
        var parts = new Func<Message>[]
        {
            () => ListingPart(Guid.NewGuid(), more: false, "b.txt"),
            () => new Message(MessageType.ClassFolderListing, ClassFolderWire.EncodeListing(PartsLesson, "sem pasta", Array.Empty<ClassFolderEntry>())),
            () => new Message(MessageType.CommandAck, CommandResult.Encode(false, "não")),
            () => new Message(MessageType.ClassFolderListing, new byte[] { 9 }),
        };

        foreach (var second in parts)
        {
            using var session = new Scripted(channel =>
            {
                channel.Send(ListingPart(PartsLesson, more: true, "a.txt"));
                channel.Send(second());
            });

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                Run(() => ClassFolderClient.List(session.Machine, session.MasterCertificate, session.Port)));
        }
    }

    // A Session that never stops sending parts is not followed for ever: no class has that many files.
    [Fact]
    public async Task List_MoreFilesThanAnyClassHas_IsRefused()
    {
        using var session = new Scripted(channel =>
        {
            channel.Send(ListingPart(PartsLesson, more: true, "a.txt", "b.txt", "c.txt"));
            channel.Send(ListingPart(PartsLesson, more: false, "d.txt", "e.txt", "f.txt"));
        });

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Run(() => ClassFolderClient.List(session.Machine, session.MasterCertificate, session.Port, maxFiles: 5)));
        Assert.Contains("arquivos demais", refused.Message);
    }

    [Fact]
    public async Task List_ARefusalWithoutADetail_StillHasAProblem()
    {
        using var session = new Scripted(channel => channel.Send(new Message(MessageType.CommandAck, new byte[] { 0 })));

        var listing = await Run(() => ClassFolderClient.List(session.Machine, session.MasterCertificate, session.Port));

        Assert.False(string.IsNullOrEmpty(listing.Problem));
        Assert.Empty(listing.Entries);
    }

    [Fact]
    public async Task Pull_AsksForTheFile_AndWritesWhatArrives()
    {
        var lesson = Guid.NewGuid();
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a/b.txt", 5);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2 }));
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 3, 4, 5 }));
            SendAck(channel, true, string.Empty);
        });
        var destination = new MemoryStream();

        var result = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, lesson, "a/b.txt", destination, session.Port));

        Assert.Equal((true, string.Empty), result);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, destination.ToArray());
        Assert.Equal(MessageType.PullClassFile, session.Request!.Type);
        Assert.True(ClassFolderWire.TryDecodeFileRef(session.Request.Payload, out var askedLesson, out var askedPath));
        Assert.Equal((lesson, "a/b.txt"), (askedLesson, askedPath));
    }

    [Fact]
    public async Task Pull_LongerThanHeader_IsRejected()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 3);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2, 3, 4 }));
            SendAck(channel, true, string.Empty);
        });
        var destination = new MemoryStream();

        var (succeeded, detail) = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", destination, session.Port));

        Assert.False(succeeded);
        Assert.NotEmpty(detail);
        Assert.Equal(0, destination.Length); // the chunk that passes the header is never written
    }

    [Fact]
    public async Task Pull_ChunksThatAddUpToMoreThanTheHeader_AreRejected()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 3);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2 }));
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 3, 4 }));
            SendAck(channel, true, string.Empty);
        });
        var destination = new MemoryStream();

        var (succeeded, _) = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", destination, session.Port));

        Assert.False(succeeded);
        Assert.True(destination.Length <= 3);
    }

    [Fact]
    public async Task Pull_AChunkAfterTheLastByte_IsRejected()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 2);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2 }));
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 3 }));
            SendAck(channel, true, string.Empty);
        });
        var destination = new MemoryStream();

        var (succeeded, _) = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", destination, session.Port));

        Assert.False(succeeded);
        Assert.Equal(2, destination.Length);
    }

    [Fact]
    public async Task Pull_ShorterThanHeader_IsRejected()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 5);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2 }));
            SendAck(channel, true, string.Empty); // "all of it arrived", with 3 bytes missing
        });

        var (succeeded, detail) = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", new MemoryStream(), session.Port));

        Assert.False(succeeded);
        Assert.NotEmpty(detail);
    }

    [Fact]
    public async Task Pull_HeaderOverTheLimit_IsRejected_BeforeAnythingIsWritten()
    {
        using var session = new Scripted(channel =>
        {
            channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("a.txt", FileTransfer.MaxFileSize + 1)));
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1 }));
        });
        var destination = new MemoryStream();

        var (succeeded, detail) = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", destination, session.Port));

        Assert.False(succeeded);
        Assert.NotEmpty(detail);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task Pull_TheFinalAnswerSaysItFailed_IsAFailure()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 1);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1 }));
            SendAck(channel, false, "o arquivo mudou");
        });

        var result = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", new MemoryStream(), session.Port));

        Assert.Equal((false, "o arquivo mudou"), result);
    }

    [Fact]
    public async Task Pull_ASessionThatDropsMidFile_ThrowsAndNothingIsReportedAsSuccess()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 100);
            channel.Send(new Message(MessageType.FileChunk, new byte[50]));
        });
        var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<IOException>(() => Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", destination, session.Port)));
    }

    [Fact]
    public async Task Pull_ADestinationThatCantBeWritten_FailsWithTheReason()
    {
        using var session = new Scripted(channel =>
        {
            SendHeader(channel, "a.txt", 1);
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1 }));
            SendAck(channel, true, string.Empty);
        });

        var (succeeded, detail) = await Run(() => ClassFolderClient.Pull(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", new MemoryStream(new byte[1], writable: false), session.Port));

        Assert.False(succeeded);
        Assert.StartsWith("não foi possível salvar o arquivo", detail);
    }

    [Fact]
    public async Task Push_SendsTheHeader_ThenTheFileInChunks()
    {
        var lesson = Guid.NewGuid();
        var content = Enumerable.Range(0, FileTransfer.ChunkSize + 10).Select(i => (byte)i).ToArray();
        var received = new List<byte>();
        var chunks = 0;
        using var session = new Scripted(channel =>
        {
            SendAck(channel, true, string.Empty); // accepted
            while (received.Count < content.Length)
            {
                var chunk = channel.Receive();
                Assert.Equal(MessageType.FileChunk, chunk.Type);
                received.AddRange(chunk.Payload);
                chunks++;
            }
            SendAck(channel, true, string.Empty); // saved
        });
        var source = session.TempFile(content);
        var time = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc).Ticks;

        var result = await Run(() => ClassFolderClient.Push(
            session.Machine, session.MasterCertificate, lesson, "x/y.bin", source, time, new FileVersion(5, 6), session.Port));

        Assert.Equal((true, string.Empty), result);
        Assert.Equal(content, received);
        Assert.Equal(2, chunks);
        Assert.Equal(MessageType.PushClassFile, session.Request!.Type);
        Assert.True(ClassFolderWire.TryDecodePushHeader(
            session.Request.Payload, out var l, out var p, out var size, out var ticks, out var replacing));
        Assert.Equal((lesson, "x/y.bin", (long)content.Length, time), (l, p, size, ticks));
        Assert.Equal((5L, 6L), replacing);
    }

    // Where the PC had no such file, the push says it may replace nothing.
    [Fact]
    public async Task Push_OntoNothing_SaysSo()
    {
        using var session = new Scripted(channel => SendAck(channel, false, "mudou neste PC"));

        var result = await Run(() => ClassFolderClient.Push(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", session.TempFile(1), DateTime.UtcNow.Ticks, null, session.Port));

        Assert.Equal((false, "mudou neste PC"), result);
        Assert.True(ClassFolderWire.TryDecodePushHeader(session.Request!.Payload, out _, out _, out _, out _, out var replacing));
        Assert.Null(replacing);
    }

    [Fact]
    public async Task ClearSetAside_SendsTheFilesDealtWith()
    {
        var lesson = Guid.NewGuid();
        var files = new[] { new ClassFolderEntry(lesson, "a.txt", 1, 2), new ClassFolderEntry(lesson, "sub/b.txt", 3, 4) };
        using var session = new Scripted(channel => SendAck(channel, true, string.Empty));

        var result = await Run(() => ClassFolderClient.ClearSetAside(session.Machine, session.MasterCertificate, lesson, files, session.Port));

        Assert.Equal((true, string.Empty), result);
        Assert.Equal(MessageType.ClearSetAside, session.Request!.Type);
        Assert.True(ClassFolderWire.TryDecodeClearSetAside(session.Request.Payload, out var sentLesson, out var sent));
        Assert.Equal(lesson, sentLesson);
        Assert.Equal(files, sent);
    }

    [Fact]
    public async Task Push_RefusedUpFront_SendsNoChunks()
    {
        var chunkArrived = false;
        using var session = new Scripted(channel =>
        {
            SendAck(channel, false, "aula diferente");
            try
            {
                channel.Receive();
                chunkArrived = true;
            }
            catch (IOException)
            {
                // The Master hung up, as it should.
            }
        });

        var result = await Run(() => ClassFolderClient.Push(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", session.TempFile(10), DateTime.UtcNow.Ticks, null, session.Port));

        Assert.Equal((false, "aula diferente"), result);
        Assert.False(chunkArrived);
    }

    [Fact]
    public async Task Push_TheFinalAnswerSaysItWasNotSaved_IsAFailure()
    {
        using var session = new Scripted(channel =>
        {
            SendAck(channel, true, string.Empty);
            channel.Receive();
            SendAck(channel, false, "disco cheio");
        });

        var result = await Run(() => ClassFolderClient.Push(
            session.Machine, session.MasterCertificate, Guid.NewGuid(), "a.txt", session.TempFile(1), DateTime.UtcNow.Ticks, null, session.Port));

        Assert.Equal((false, "disco cheio"), result);
    }

    private static void SendHeader(SecureChannel channel, string path, long size) =>
        channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(path, size)));

    private static void SendAck(SecureChannel channel, bool ok, string detail) =>
        channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(ok, detail)));

    // Stands in for a Session from before the class folder, or one a student
    // rewrote: answers the first message with whatever the test scripts,
    // then hangs up. Request is what the Master asked.
    private sealed class Scripted : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _sessionCertificate = SelfSignedCertificate.Create("scripted-session");
        private readonly List<string> _tempFiles = new();
        private volatile Message? _request;

        public Scripted(Action<SecureChannel> script)
        {
            MasterCertificate = SelfSignedCertificate.Create("master-under-test");
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            // Its own thread: a busy test run's pool can hold a blocking accept back past the Master's timeouts.
            Task.Factory.StartNew(() =>
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    using var channel = SecureChannel.AuthenticateAsServer(
                        client, _sessionCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }));
                    _request = channel.Receive();
                    script(channel);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    // The Master hung up, or Dispose stopped the listener.
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public int Port { get; }

        public X509Certificate2 MasterCertificate { get; }

        public KnownMachine Machine => new("Aluno-1", "127.0.0.1", _sessionCertificate.Thumbprint, null);

        public Message? Request => _request;

        public string TempFile(int length) => TempFile(new byte[length]);

        public string TempFile(byte[] content)
        {
            var path = Path.Combine(Path.GetTempPath(), $"midview-classsource-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, content);
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            _listener.Stop();
            _sessionCertificate.Dispose();
            MasterCertificate.Dispose();
            foreach (var path in _tempFiles)
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
