using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Session;

internal static class InputEventCodec
{
    public static InputEvent Decode(byte[] payload)
    {
        var kind = (InputEventKind)payload[0];
        var x = BitConverter.ToInt32(payload, 1);
        var y = BitConverter.ToInt32(payload, 5);
        var vk = BitConverter.ToUInt16(payload, 9);
        return new InputEvent(kind, x, y, vk);
    }

    public static byte[] Encode(InputEvent inputEvent)
    {
        var buffer = new byte[11];
        buffer[0] = (byte)inputEvent.Kind;
        BitConverter.GetBytes(inputEvent.X).CopyTo(buffer, 1);
        BitConverter.GetBytes(inputEvent.Y).CopyTo(buffer, 5);
        BitConverter.GetBytes(inputEvent.VirtualKeyCode).CopyTo(buffer, 9);
        return buffer;
    }
}

internal static class TileMessageCodec
{
    public static byte[] Encode(EncodedTile tile)
    {
        var buffer = new byte[8 + tile.JpegBytes.Length];
        BitConverter.GetBytes(tile.Row).CopyTo(buffer, 0);
        BitConverter.GetBytes(tile.Column).CopyTo(buffer, 4);
        tile.JpegBytes.CopyTo(buffer, 8);
        return buffer;
    }

    public static (int Row, int Column, byte[] JpegBytes) Decode(byte[] payload)
    {
        var row = BitConverter.ToInt32(payload, 0);
        var column = BitConverter.ToInt32(payload, 4);
        var jpegBytes = payload[8..];
        return (row, column, jpegBytes);
    }
}

public sealed class SessionServer
{
    private const int HeartbeatCheckIntervalMs = 1000;

    // Capture cadence is fully decoupled from message traffic (mouse-move
    // events alone can arrive far faster than any capture pipeline should
    // run). ~15 fps for a viewer from before ScreenFrame, which drew one tile
    // at a time and couldn't keep up with more; ScreenFrame viewers follow
    // CaptureCadence instead.
    private const int CaptureIntervalMs = 66;

    // How long the ScreenFrame loop waits for the viewer to acknowledge
    // before giving up on an update and carrying on (see FrameCredit).
    private static readonly TimeSpan DefaultFrameAckTimeout = TimeSpan.FromSeconds(1);

    private const int CaptureRetryDelayMs = 500;

    // Plenty for a real LAN handshake plus the first message. Anything on
    // the network can open a TCP connection and then send nothing; without
    // a timeout that alone kept every Master out.
    private const int HandshakeTimeoutMs = 10_000;

    // A controlling Master sends a heartbeat every second for as long as its
    // window is open, and a preview connection asks for a thumbnail every
    // few seconds, so this much silence means the Master is gone (powered
    // off, cable pulled) without closing the connection. Without a timeout, Receive() would wait
    // forever and no Master could ever connect again. Also how long a new
    // Master waits for a previous connection to end before giving up.
    private const int ConnectionTimeoutMs = 15_000;

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly IScreenCapture _screenCapture;
    private readonly IInputInjector _inputInjector;
    private readonly InputLockManager _inputLockManager;
    private readonly IMessageDisplay? _messageDisplay;
    private readonly IChatWindows? _chatWindows;
    private readonly FileInbox? _fileInbox;
    private readonly IAppCloser? _appCloser;
    private readonly IOpenProgramsLister? _openPrograms;
    private readonly DeliveryFolder? _deliveryFolder;
    private readonly IScreenLockDisplay? _screenLock;
    private readonly IFileOpener? _fileOpener;
    private readonly IRecycleBin? _recycleBin;
    private readonly ICountdownNotice? _countdownNotice;
    private readonly IWebsiteOpener? _websiteOpener;
    private readonly IProgramStarter? _programStarter;
    private readonly TimeSpan _frameAckTimeout;

    // One controlling Master served at a time: the input lock and the
    // injected input belong to the machine, not to a connection. Preview
    // connections (the classroom console's thumbnails) don't take this slot.
    private readonly SemaphoreSlim _servingSlot = new(1, 1);

    // How many screen-lock connections are open: the lock screen stays up
    // until the last one ends.
    private readonly object _screenLockGate = new();
    private int _screenLocks;

    public SessionServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        IScreenCapture screenCapture, IInputInjector inputInjector, InputLockManager inputLockManager,
        IMessageDisplay? messageDisplay = null, IChatWindows? chatWindows = null, FileInbox? fileInbox = null,
        IAppCloser? appCloser = null, DeliveryFolder? deliveryFolder = null, IOpenProgramsLister? openPrograms = null,
        IScreenLockDisplay? screenLock = null, IFileOpener? fileOpener = null, IRecycleBin? recycleBin = null,
        ICountdownNotice? countdownNotice = null, IWebsiteOpener? websiteOpener = null,
        IProgramStarter? programStarter = null, TimeSpan? frameAckTimeout = null)
    {
        _frameAckTimeout = frameAckTimeout ?? DefaultFrameAckTimeout;
        _countdownNotice = countdownNotice;
        _websiteOpener = websiteOpener;
        _programStarter = programStarter;
        _screenLock = screenLock;
        _fileOpener = fileOpener;
        _recycleBin = recycleBin;
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _screenCapture = screenCapture;
        _inputInjector = inputInjector;
        _inputLockManager = inputLockManager;
        _messageDisplay = messageDisplay;
        _chatWindows = chatWindows;
        _fileInbox = fileInbox;
        _appCloser = appCloser;
        _deliveryFolder = deliveryFolder;
        _openPrograms = openPrograms;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            // Nagle's algorithm batches/delays small writes waiting to
            // coalesce them -- exactly wrong for this protocol's frequent
            // tiny messages (mouse moves, heartbeats, tile updates), adding
            // real latency to both directions regardless of how fast either
            // side processes them.
            tcpClient.NoDelay = true;
            // Off the accept loop, so a connection stuck in its TLS handshake
            // can't keep the next one from being accepted -- and on a thread of
            // its own, since every handler blocks for as long as its
            // connection lasts (a whole lesson, for previews and chat): on
            // thread-pool tasks, those would starve the pool.
            _ = Task.Factory.StartNew(
                () => HandleClientSafely(tcpClient, cancellationToken), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void HandleClientSafely(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        try
        {
            HandleClient(tcpClient, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling a client connection: {ex.Message}");
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        client.ReceiveTimeout = HandshakeTimeoutMs;
        client.SendTimeout = HandshakeTimeoutMs;
        using var channel = SecureChannel.AuthenticateAsServer(client, _serverCertificate, _masterTrustStore);

        // The first message says what kind of connection this is. Every
        // Master keeps one preview connection open to each machine in its
        // grid, so if previews were served like control connections, the
        // one-at-a-time slot below would always be taken and full control
        // could never get in. Anything not handled below is a control
        // connection, and its first message also picks how the screen is
        // sent: ScreenStream (ViewControlWindow now) for ScreenFrame updates,
        // StartControl or a Heartbeat (a Master from before that) for one
        // FrameTile per tile. So a new one-shot type must never be one a
        // control connection opens with.
        var firstMessage = channel.Receive();

        client.ReceiveTimeout = ConnectionTimeoutMs;
        client.SendTimeout = ConnectionTimeoutMs;

        // A teacher message is a one-shot command, like the Agent's: shown,
        // acknowledged, done. Like previews, it never needs the control slot.
        if (firstMessage.Type == MessageType.ShowMessage)
        {
            var shown = ShowTeacherMessage(firstMessage.Payload);
            channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, new[] { (byte)(shown ? 1 : 0) }));
            return;
        }

        if (firstMessage.Type == MessageType.RequestThumbnail)
        {
            ServePreview(channel, firstMessage, cancellationToken);
            return;
        }

        // Like messages, files are one-shot and never need the control slot.
        if (firstMessage.Type is MessageType.SendFile or MessageType.SendFileWithOptions)
        {
            ReceiveFile(channel, firstMessage);
            return;
        }

        if (firstMessage.Type == MessageType.ClearDeliveryFolder)
        {
            var (cleared, detail) = ClearDeliveryFolder();
            channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(cleared, detail)));
            return;
        }

        if (firstMessage.Type == MessageType.CollectFiles)
        {
            SendCollectedFiles(channel);
            return;
        }

        // "Abrir programa": one-shot too.
        if (firstMessage.Type == MessageType.OpenProgram)
        {
            var (started, detail) = OpenProgram(firstMessage.Payload);
            channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(started, detail)));
            return;
        }

        // "Abrir site": one-shot, like a message.
        if (firstMessage.Type == MessageType.OpenWebsite)
        {
            var (opened, detail) = OpenWebsite(firstMessage.Payload);
            channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(opened, detail)));
            return;
        }

        if (firstMessage.Type == MessageType.CloseApps)
        {
            var (closed, detail) = CloseApps(firstMessage.Payload);
            channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(closed, detail)));
            return;
        }

        // Only suggests names to the teacher's block window; one-shot, never
        // the control slot.
        if (firstMessage.Type == MessageType.ListOpenPrograms)
        {
            var programs = _openPrograms?.List() ?? Array.Empty<OpenProgram>();
            channel.Send(new Midview.Protocol.Message(MessageType.OpenPrograms, OpenProgramsReport.Encode(programs)));
            return;
        }

        // A chat can stay open for the whole lesson, alongside full control
        // of the same machine, so it doesn't take the control slot either.
        if (firstMessage.Type == MessageType.ChatOpen)
        {
            ServeChat(channel, cancellationToken);
            return;
        }

        // Nor does a screen lock, which lasts as long as its connection.
        if (firstMessage.Type == MessageType.LockScreen)
        {
            ServeScreenLock(channel, cancellationToken);
            return;
        }

        if (!_servingSlot.Wait(ConnectionTimeoutMs, cancellationToken))
        {
            Console.WriteLine("Another MidviewMaster is still connected; closing the new connection.");
            return;
        }

        try
        {
            Serve(client, channel, firstMessage, cancellationToken);
        }
        finally
        {
            _servingSlot.Release();
        }
    }

    // Never touches the input lock, the capture thread or FramePipeline:
    // a preview must work the same whether or not anyone is controlling
    // this machine.
    private void ServePreview(
        SecureChannel channel, Midview.Protocol.Message firstMessage, CancellationToken cancellationToken)
    {
        var message = firstMessage;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (message.Type == MessageType.RequestThumbnail)
            {
                channel.Send(new Midview.Protocol.Message(MessageType.ThumbnailFrame, RenderThumbnailOrEmpty()));
            }
            message = channel.Receive();
        }
    }

    // Always answers, even when capture fails (e.g. while the screen is
    // locked): an empty reply keeps requests and replies paired, so the
    // Master can show "screen unavailable" instead of treating the machine
    // as offline and reconnecting on every poll.
    private byte[] RenderThumbnailOrEmpty()
    {
        try
        {
            return ThumbnailRenderer.Render(_screenCapture);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thumbnail capture failed: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    // This thread only receives; the chat window's thread only sends (the
    // student's lines) -- the one reader/one writer pattern SslStream
    // supports, with a lock in case the window ever sends from two threads.
    private void ServeChat(SecureChannel channel, CancellationToken cancellationToken)
    {
        if (_chatWindows is null)
        {
            return;
        }

        var sendLock = new object();
        var window = _chatWindows.Open(text =>
        {
            try
            {
                lock (sendLock)
                {
                    channel.Send(new Midview.Protocol.Message(MessageType.ChatText, TextMessage.Encode(text)));
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return false;
            }
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = channel.Receive();
                // Heartbeats only keep the connection alive; nothing else to do.
                if (message.Type == MessageType.ChatText && TextMessage.Decode(message.Payload) is { } text)
                {
                    window.ShowTeacherMessage(text);
                }
            }
        }
        finally
        {
            window.ShowConversationEnded();
        }
    }

    // Locked from the answer until the connection ends, however it ends:
    // closed by the teacher ("Desbloquear", or the program closing), broken,
    // or silent for ConnectionTimeoutMs because the teacher's PC stopped
    // answering -- the class is never left locked. Only heartbeats are
    // expected meanwhile.
    private void ServeScreenLock(SecureChannel channel, CancellationToken cancellationToken)
    {
        if (_screenLock is null)
        {
            channel.Send(new Midview.Protocol.Message(
                MessageType.CommandAck, CommandResult.Encode(false, "esta máquina não bloqueia a tela")));
            return;
        }

        try
        {
            _inputLockManager.HoldForScreenLock();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Locking input for the screen lock failed: {ex.Message}");
            channel.Send(new Midview.Protocol.Message(
                MessageType.CommandAck, CommandResult.Encode(false, "o teclado e o mouse não puderam ser travados")));
            return;
        }

        try
        {
            ShowScreenLock();
            try
            {
                channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(true, "tela bloqueada")));
                while (!cancellationToken.IsCancellationRequested)
                {
                    channel.Receive();
                }
            }
            finally
            {
                HideScreenLock();
            }
        }
        finally
        {
            _inputLockManager.ReleaseScreenLock();
        }
    }

    private void ShowScreenLock()
    {
        lock (_screenLockGate)
        {
            // Counted only once it's shown, so a Show that fails leaves no
            // lock behind for a later one to wait on.
            if (_screenLocks == 0)
            {
                _screenLock!.Show();
            }
            _screenLocks++;
        }
    }

    private void HideScreenLock()
    {
        lock (_screenLockGate)
        {
            if (--_screenLocks == 0)
            {
                _screenLock!.Hide();
            }
        }
    }

    // Accepted or refused up front (a first CommandAck), before the Master
    // streams anything -- a refusal sent mid-stream would likely be lost to
    // a connection reset. A second CommandAck reports how it ended.
    private void ReceiveFile(SecureChannel channel, Midview.Protocol.Message header)
    {
        string? refusal;
        var fileName = string.Empty;
        long size = 0;
        var options = SendFileOptions.Default;
        if (_fileInbox is null)
        {
            refusal = "esta máquina não recebe arquivos";
        }
        else if (!(header.Type == MessageType.SendFileWithOptions
                     ? FileTransfer.TryDecodeOptionsHeader(header.Payload, out options, out fileName, out size)
                     : FileTransfer.TryDecodeHeader(header.Payload, out fileName, out size)))
        {
            refusal = "pedido de envio inválido";
        }
        else
        {
            refusal = _fileInbox.RefusalFor(size);
        }

        channel.Send(new Midview.Protocol.Message(
            MessageType.CommandAck, CommandResult.Encode(refusal is null, refusal ?? string.Empty)));
        if (refusal is not null)
        {
            return;
        }

        var (saved, detail, note) = SaveIncomingFile(channel, fileName, size, options.Overwrite);
        if (!saved)
        {
            channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(false, detail)));
            return;
        }

        var savedName = detail;
        var (opened, afterNote) = AfterSaving(options.AfterSend, savedName);
        channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(true,
            savedName + (note is null ? string.Empty : $" ({note})") + (afterNote is null ? string.Empty : $"; {afterNote}"))));

        // What opens on screen tells the student by itself.
        if (!opened)
        {
            TryNotify($"O professor enviou o arquivo \"{savedName}\".{Environment.NewLine}" +
                      $"Ele foi salvo em: {_fileInbox!.DirectoryPath}");
        }
    }

    // Opens the file or the folder when the teacher asked to; never a program
    // (FileOpenPolicy). Returns whether something opened, and a note for the
    // teacher about it.
    private (bool Opened, string? Note) AfterSaving(AfterSend afterSend, string savedName)
    {
        if (afterSend == AfterSend.Nothing)
        {
            return (false, null);
        }
        if (_fileOpener is null)
        {
            return (false, "esta máquina não abre arquivos");
        }
        if (afterSend == AfterSend.OpenFile && !FileOpenPolicy.MayOpen(savedName))
        {
            return (false, "programas não são abertos automaticamente");
        }

        var folder = _fileInbox!.DirectoryPath;
        try
        {
            if (afterSend == AfterSend.OpenFolder)
            {
                _fileOpener.Open(folder);
                return (true, "pasta aberta no aluno");
            }
            _fileOpener.Open(Path.Combine(folder, savedName));
            return (true, "aberto no aluno");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Opening for the student failed: {ex.Message}");
            return (false, $"não abriu: {ErrorText.Describe(ex)}");
        }
    }

    // Checked again here, whatever the Master checked: only a website is
    // ever handed to Windows to open, never a file or a program.
    private (bool Opened, string Detail) OpenWebsite(byte[] payload)
    {
        if (_websiteOpener is null)
        {
            return (false, "esta máquina não abre sites");
        }
        if (!WebsiteAddress.TryParse(TextMessage.Decode(payload), out var address, out _))
        {
            return (false, "não é o endereço de um site");
        }

        try
        {
            _websiteOpener.Open(address);
            return (true, "aberto");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Opening a website for the student failed: {ex.Message}");
            return (false, $"o navegador não abriu: {ErrorText.Describe(ex)}");
        }
    }

    // As the student, so nothing the student couldn't start themselves.
    // Variables (%USERPROFILE%...) are the student's, expanded here.
    private (bool Started, string Detail) OpenProgram(byte[] payload)
    {
        if (_programStarter is null)
        {
            return (false, "esta máquina não abre programas");
        }
        if (!ProgramCommand.TryParse(TextMessage.Decode(payload), out var program, out var arguments, out _))
        {
            return (false, "comando inválido");
        }

        try
        {
            return _programStarter.Start(Environment.ExpandEnvironmentVariables(program), Environment.ExpandEnvironmentVariables(arguments))
                ? (true, "aberto")
                : (true, "aberto, mas ainda esperando na máquina (talvez pedindo a senha de administrador)");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return (false, $"programa não encontrado: {program}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "o pedido de administrador foi recusado na máquina");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Starting a program for the student failed: {ex.Message}");
            return (false, $"não abriu: {ErrorText.Describe(ex)}");
        }
    }

    // Only the teacher's "Apagar as pastas dos alunos": empties the
    // delivery folder into the student's Recycle Bin and tells them.
    private (bool Cleared, string Detail) ClearDeliveryFolder()
    {
        if (_deliveryFolder is null || _recycleBin is null)
        {
            return (false, "esta máquina não esvazia a pasta de entrega");
        }

        var (recycled, problems) = _deliveryFolder.MoveAllToRecycleBin(_recycleBin);
        if (recycled > 0)
        {
            TryNotify("O professor esvaziou a pasta \"Entregar ao professor\". O que estava nela foi para a Lixeira.");
        }
        var detail = recycled == 0 && problems.Count == 0
            ? "a pasta já estava vazia"
            : $"{recycled} item(ns) foram para a Lixeira do aluno"
              + (problems.Count > 0 ? $"; não saiu: {string.Join(", ", problems)}" : string.Empty);
        return (problems.Count == 0, detail);
    }

    private (bool Saved, string Detail, string? Note) SaveIncomingFile(
        SecureChannel channel, string fileName, long size, bool overwrite)
    {
        try
        {
            using var incoming = _fileInbox!.Begin(fileName);
            long received = 0;
            while (received < size)
            {
                var chunk = channel.Receive();
                if (chunk.Type != MessageType.FileChunk || chunk.Payload.Length == 0
                    || received + chunk.Payload.Length > size)
                {
                    return (false, "a transferência chegou incompleta ou corrompida", null);
                }
                incoming.Write(chunk.Payload);
                received += chunk.Payload.Length;
            }
            var (name, note) = incoming.Complete(overwrite);
            return (true, name, note);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"não foi possível salvar o arquivo: {ErrorText.Describe(ex)}", null);
        }
    }

    // The files go out one after another, with no answer from the Master in
    // between. A file that can't be read (open without sharing, say) or that
    // would pass the limits is left out and listed in the final CommandAck;
    // one that fails midway can't be taken back, so the connection just
    // drops and the Master discards it.
    private void SendCollectedFiles(SecureChannel channel)
    {
        if (_deliveryFolder is null)
        {
            channel.Send(new Midview.Protocol.Message(
                MessageType.CommandAck, CommandResult.Encode(false, "esta máquina não entrega arquivos")));
            return;
        }

        IReadOnlyList<CollectableFile> files;
        try
        {
            files = _deliveryFolder.ListFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            channel.Send(new Midview.Protocol.Message(
                MessageType.CommandAck, CommandResult.Encode(false, $"a pasta de entrega não pôde ser lida: {ErrorText.Describe(ex)}")));
            return;
        }

        var sent = 0;
        long bytes = 0;
        var notSent = new List<string>();
        var buffer = new byte[FileTransfer.ChunkSize];
        foreach (var file in files)
        {
            if (sent == _deliveryFolder.MaxFiles)
            {
                break;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(file.RelativePath) > FileTransfer.MaxNameBytes)
            {
                notSent.Add($"{file.RelativePath} (caminho longo demais)");
                continue;
            }

            FileStream stream;
            try
            {
                stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notSent.Add($"{file.RelativePath} (não pôde ser lido)");
                continue;
            }

            using (stream)
            {
                var size = stream.Length;
                if (bytes + size > _deliveryFolder.MaxTotalBytes)
                {
                    notSent.Add($"{file.RelativePath} (passaria do limite de {_deliveryFolder.MaxTotalBytes / (1024 * 1024)} MB)");
                    continue;
                }

                channel.Send(new Midview.Protocol.Message(MessageType.SendFile, FileTransfer.EncodeHeader(file.RelativePath, size)));
                for (var remaining = size; remaining > 0;)
                {
                    var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0)
                    {
                        throw new IOException($"{file.RelativePath} diminuiu durante o envio");
                    }
                    channel.Send(new Midview.Protocol.Message(MessageType.FileChunk, buffer.AsSpan(0, read).ToArray()));
                    remaining -= read;
                }
                sent++;
                bytes += size;
            }
        }

        // The listing stops just past the limit (see DeliveryFolder.ListFiles),
        // so a full one means files went unsent -- even ones never listed,
        // when some listed ones were left out above.
        if (files.Count > _deliveryFolder.MaxFiles)
        {
            notSent.Add($"o que passou de {_deliveryFolder.MaxFiles} arquivos");
        }

        var detail = notSent.Count == 0
            ? string.Empty
            : $"{notSent.Count} não enviado(s): {string.Join(", ", notSent.Take(3))}{(notSent.Count > 3 ? $" e mais {notSent.Count - 3}" : string.Empty)}";
        channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(true, detail)));

        var notice = sent > 0
            ? $"O professor recolheu {sent} arquivo(s) da pasta \"Entregar ao professor\"."
            : "O professor pediu os arquivos da pasta \"Entregar ao professor\", mas nenhum foi enviado.";
        if (detail.Length > 0)
        {
            notice += $"{Environment.NewLine}{char.ToUpperInvariant(detail[0])}{detail[1..]}.";
        }
        if (sent == 0)
        {
            notice += $"{Environment.NewLine}A pasta fica em: {_deliveryFolder.DirectoryPath}";
        }
        TryNotify(notice);
    }

    // The student is always told first. With a warning time, the answer goes
    // back right away and the programs close when it's up, on their own: the
    // teacher's window may be long closed by then.
    private (bool Closed, string Detail) CloseApps(byte[] payload)
    {
        if (_appCloser is null)
        {
            return (false, "esta máquina não fecha programas");
        }
        if (!CloseAppsCommand.TryDecode(payload, out var mode, out var delaySeconds))
        {
            return (false, "pedido inválido");
        }

        var force = mode == CloseAppsMode.Force;
        if (delaySeconds == 0)
        {
            TryNotify(force ? "O professor encerrou seus programas." : "O professor está fechando seus programas.");
            return CloseAll(force);
        }

        // The time left counts down on the student's screen; without the
        // banner, a message says it once.
        var when = CloseAppsCommand.DelayText(delaySeconds);
        if (_countdownNotice is not null)
        {
            var (running, done) = CountdownText.CloseApps(force);
            TryCountdown(running, done, TimeSpan.FromSeconds(delaySeconds));
        }
        else
        {
            TryNotify($"O professor vai fechar seus programas em {when}. Salve seu trabalho agora"
                      + (force ? ": o que não estiver salvo será perdido." : "."));
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            var (closed, detail) = CloseAll(force);
            Console.WriteLine($"Closing programs after the warning: {(closed ? "" : "failed, ")}{detail}");
        });
        return (true, $"aluno avisado; os programas fecham em {when}");
    }

    private (bool Closed, string Detail) CloseAll(bool force)
    {
        try
        {
            var (asked, forced) = _appCloser!.CloseAll(force);
            var detail = asked == 0 && forced == 0
                ? "nenhum programa para fechar"
                : forced == 0
                    ? $"pedido de fechamento enviado a {asked} janela(s)"
                    : asked == 0
                        ? $"{forced} programa(s) encerrado(s) à força"
                        : $"pedido enviado a {asked} janela(s); {forced} programa(s) encerrado(s) à força";
            return (true, detail);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Closing programs failed: {ex.Message}");
            return (false, $"falhou: {ErrorText.Describe(ex)}");
        }
    }

    private void TryCountdown(string message, string doneMessage, TimeSpan duration)
    {
        try
        {
            _countdownNotice!.Show(message, doneMessage, duration);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Showing a countdown failed: {ex.Message}");
        }
    }

    private void TryNotify(string text)
    {
        try
        {
            _messageDisplay?.Show(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Showing a notification failed: {ex.Message}");
        }
    }

    private bool ShowTeacherMessage(byte[] payload)
    {
        var text = TextMessage.Decode(payload);
        if (text is null || _messageDisplay is null)
        {
            return false;
        }

        try
        {
            _messageDisplay.Show(text);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Showing a teacher message failed: {ex.Message}");
            return false;
        }
    }

    private void Serve(
        TcpClient tcpClient, SecureChannel channel, Midview.Protocol.Message firstMessage, CancellationToken cancellationToken)
    {
        var isControlling = false;

        // An exception escaping a Timer callback would take the whole process down.
        using var heartbeatTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                _inputLockManager.CheckHeartbeatTimeout();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Heartbeat check failed: {ex.Message}");
            }
        }, null, HeartbeatCheckIntervalMs, HeartbeatCheckIntervalMs);

        // A fresh FramePipeline per connection: it isn't thread-safe, and
        // reusing one server-wide let a slow-to-stop capture thread from a
        // previous connection race a new one over the same mutable state
        // after a reconnect (also left the diff starting from a stale frame).
        var framePipeline = new FramePipeline(_screenCapture);

        // A viewer that opens with ScreenStream gets ScreenFrame updates it
        // acknowledges; any other first message is a viewer from before
        // that, which gets one FrameTile per tile.
        var credit = firstMessage.Type == MessageType.ScreenStream ? new FrameCredit() : null;
        var cadence = new CaptureCadence();

        // The capture loop runs on its own thread, independent of how often
        // messages arrive on the connection below. The two threads never
        // touch the channel at the same time from opposite ends of a single
        // call: this thread only sends (FrameTile or ScreenFrame), the loop
        // below only receives -- the one reader/one writer pattern SslStream
        // supports without locking.
        using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var captureThread = new Thread(() =>
        {
            if (credit is null)
            {
                CaptureLoop(framePipeline, channel, tcpClient, captureCts);
            }
            else
            {
                StreamScreenFrames(framePipeline, channel, tcpClient, captureCts, credit, cadence);
            }
        }) { IsBackground = true };
        captureThread.Start();

        try
        {
            var messageCount = 0;
            var maxGapMs = 0.0;
            var gapSw = System.Diagnostics.Stopwatch.StartNew();
            var acks = new AckSummary();

            Midview.Protocol.Message? pendingMessage = firstMessage;
            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                var receiveSw = System.Diagnostics.Stopwatch.StartNew();
                var message = pendingMessage ?? channel.Receive();
                pendingMessage = null;
                var receiveMs = receiveSw.Elapsed.TotalMilliseconds;

                var gapMs = gapSw.Elapsed.TotalMilliseconds;
                gapSw.Restart();
                maxGapMs = Math.Max(maxGapMs, gapMs);
                messageCount++;
                if (messageCount % 50 == 0)
                {
                    SessionDiagnostics.Log(
                        $"messages received={messageCount} lastReceiveBlockedFor={receiveMs:F1}ms maxGapSinceLast50={maxGapMs:F1}ms");
                    maxGapMs = 0.0;
                }

                switch (message.Type)
                {
                    case MessageType.StartControl:
                        isControlling = true;
                        _inputLockManager.StartLock();
                        break;

                    case MessageType.StopControl:
                        isControlling = false;
                        _inputLockManager.StopLock();
                        break;

                    case MessageType.Heartbeat:
                        _inputLockManager.OnHeartbeatReceived();
                        break;

                    case MessageType.InputEvent:
                        if (isControlling)
                        {
                            _inputInjector.Inject(InputEventCodec.Decode(message.Payload));
                            cadence.Activity(Environment.TickCount64);
                        }
                        break;

                    case MessageType.FrameAck:
                        if (credit is not null && ScreenFrame.DecodeAck(message.Payload) is { } frameId)
                        {
                            var now = Environment.TickCount64;
                            acks.Add(credit.Acknowledge(frameId, now), now);
                        }
                        break;
                }
            }
        }
        finally
        {
            captureCts.Cancel();
            captureThread.Join(TimeSpan.FromSeconds(2));
            _inputLockManager.StopLock(); // safety net: connection ended for any reason
        }
    }

    private void CaptureLoop(
        FramePipeline framePipeline, SecureChannel channel, TcpClient tcpClient, CancellationTokenSource captureCts)
    {
        try
        {
            while (!captureCts.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                IReadOnlyList<EncodedTile> tiles;
                try
                {
                    tiles = framePipeline.CaptureNextFrame();
                }
                catch (Exception ex)
                {
                    // Capturing fails while the secure desktop is up (Win+L,
                    // a UAC prompt, Ctrl+Alt+Del) and works again once it's
                    // gone. Retrying keeps the stream alive; giving up would
                    // leave the teacher looking at a frozen screen with no
                    // error, since the connection itself stays open.
                    Console.WriteLine($"Screen capture failed, retrying: {ex.Message}");
                    captureCts.Token.WaitHandle.WaitOne(CaptureRetryDelayMs);
                    continue;
                }

                foreach (var tile in tiles)
                {
                    channel.Send(new Midview.Protocol.Message(MessageType.FrameTile, TileMessageCodec.Encode(tile)));
                }

                var remaining = CaptureIntervalMs - (int)sw.ElapsedMilliseconds;
                if (remaining > 0)
                {
                    captureCts.Token.WaitHandle.WaitOne(remaining);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only a failed send gets here, so the connection is dead. Closing
            // the socket is what actually ends the receive loop's blocked
            // Receive() -- it isn't watching any cancellation token.
            Console.WriteLine($"Capture loop stopped: {ex.Message}");
            tcpClient.Close();
        }
    }

    // CaptureLoop for a ScreenFrame viewer: one message per update (more
    // only for an update too big for one, see ScreenFrame.Split), only what
    // changed, never more than FrameCredit.MaxUnacknowledged updates on their
    // way before capturing again, at CaptureCadence's pace.
    private void StreamScreenFrames(
        FramePipeline framePipeline, SecureChannel channel, TcpClient tcpClient, CancellationTokenSource captureCts,
        FrameCredit credit, CaptureCadence cadence)
    {
        try
        {
            while (credit.WaitForRoom(_frameAckTimeout, captureCts.Token))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                ScreenUpdate update;
                try
                {
                    update = framePipeline.CaptureNextUpdate();
                }
                catch (Exception ex)
                {
                    // As in CaptureLoop: the secure desktop is up for now.
                    Console.WriteLine($"Screen capture failed, retrying: {ex.Message}");
                    captureCts.Token.WaitHandle.WaitOne(CaptureRetryDelayMs);
                    continue;
                }

                var now = Environment.TickCount64;
                cadence.Captured(update.Regions.Count > 0, now);
                var groups = ScreenFrame.Split(update.Regions);
                var firstId = groups.Count > 0 ? credit.Send(now, groups.Count) : 0;
                var messages = groups
                    .Select((regions, i) => new ScreenFrame(firstId + i, update.Width, update.Height, regions).Encode())
                    .ToList();
                var workMs = (int)sw.ElapsedMilliseconds;
                foreach (var payload in messages)
                {
                    channel.Send(new Midview.Protocol.Message(MessageType.ScreenFrame, payload));
                }

                var rest = cadence.RestMs(workMs, (int)sw.ElapsedMilliseconds, Environment.TickCount64);
                if (rest > 0)
                {
                    captureCts.Token.WaitHandle.WaitOne(rest);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As in CaptureLoop: only a failed send gets here.
            Console.WriteLine($"Capture loop stopped: {ex.Message}");
            tcpClient.Close();
        }
    }

    // How long the viewer takes to acknowledge updates, from sending one to
    // its FrameAck: capture to the teacher's screen and back, the delay the
    // teacher actually sees. Logged once a second rather than per update.
    private sealed class AckSummary
    {
        private int _count;
        private long _totalMs;
        private long _maxMs;
        private long _sinceMs = Environment.TickCount64;

        public void Add(long? roundTripMs, long nowMs)
        {
            if (roundTripMs is { } ms)
            {
                _count++;
                _totalMs += ms;
                _maxMs = Math.Max(_maxMs, ms);
            }

            if (_count > 0 && nowMs - _sinceMs >= 1000)
            {
                SessionDiagnostics.Log(
                    $"viewer acknowledged={_count} average={_totalMs / _count}ms max={_maxMs}ms");
                _count = 0;
                _totalMs = 0;
                _maxMs = 0;
                _sinceMs = nowMs;
            }
        }
    }
}
