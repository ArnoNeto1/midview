using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver on the PC that keeps the class folders: its store going
// to a colleague's PC that takes them over (for ClassFolderHost, which has
// checked that PC is a colleague), and standing down once the room's Agents
// keep that PC as the home.
public sealed partial class ClassFolderHandOver
{
    // What the home answers when a file changed while it was being sent (the store is read again next time).
    public const string ChangedText = "Um arquivo das turmas mudou durante a cópia no PC que guarda as pastas. Tente de novo.";

    // Answers RemoteHandOver on channel (see the class comment), on the host's
    // thread. Refused while a lesson is on, before it starts and again before the
    // last answer (one started meanwhile), and when the store can't be read
    // whole. A file that changed after the manifest was made stops the copy,
    // saying so. stop (the host closing) breaks the copy off, between two
    // chunks: the connection closes with the copy unfinished. The network's
    // exceptions go up (the host lets the connection go).
    internal void Send(SecureChannel channel, CancellationToken stop)
    {
        if (_sync.Lesson.IsOn)
        {
            channel.Send(Refusal(ClassFolderText.EndLessonFirst));
            return;
        }

        IReadOnlyList<string> classes;
        IReadOnlyList<HandOverFile> files;
        try
        {
            (classes, files) = _store.HandOverContents();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The class folders could not be read for a hand-over: {ex.Message}");
            channel.Send(Refusal(ClassFolderRemote.UnreadableText + ErrorText.Describe(ex)));
            return;
        }
        catch (ArgumentException ex)
        {
            // A link put in a class's folder between its listing and the path of a file in it.
            _log($"The class folders changed while read for a hand-over: {ex.Message}");
            channel.Send(Refusal(ChangedText));
            return;
        }

        var manifest = EncodeManifest(classes, files.Select(file => (file.WirePath, file.Size, file.LastWriteTicks)).ToList());
        using (var content = new MemoryStream(manifest))
        {
            if (!SendOne(channel, ManifestPath, content, manifest.Length, stop))
            {
                return;
            }
        }

        foreach (var file in files)
        {
            FileStream content;
            try
            {
                content = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                channel.Send(Stopped(ex is FileNotFoundException or DirectoryNotFoundException
                    ? ChangedText
                    : ClassFolderRemote.UnreadableText + ErrorText.Describe(ex)));
                return;
            }

            using (content)
            {
                // Shared for reading only: it can't change while it is sent, but it may have since the manifest.
                if (content.Length != file.Size || File.GetLastWriteTimeUtc(file.FullPath).Ticks != file.LastWriteTicks)
                {
                    channel.Send(Stopped(ChangedText));
                    return;
                }
                if (!SendOne(channel, file.WirePath, content, file.Size, stop))
                {
                    return;
                }
            }
        }

        if (_sync.Lesson.IsOn)
        {
            channel.Send(Refusal(ClassFolderText.EndLessonFirst));
            return;
        }
        var classFiles = files.Count(file => file.WirePath.StartsWith(FilesPrefix, StringComparison.Ordinal));
        channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, $"{classFiles} arquivos")));
    }

    // The sender (its certificate's thumbprint, from the TLS channel) says it
    // took the folders over, as name. Only when one of the room's online Agents
    // keeps it as the home (that certificate, and that name: never a home
    // recorded with none, which any name would match), newer than this PC, and
    // no lesson is on here (Ruling 17: some PCs still follow it, and this PC
    // keeps the folders until it ends): this PC stands down
    // (ClassFolderSync.StandDownAsync), keeps it as the other home seen
    // (ClassHomeFileMaster), and moves its store aside to "Turmas (passadas para
    // <the name the Agents recorded>)". Ok then, with the folder it went to
    // (empty: there was nothing to move), or NotMovedMark and ErrorText's words
    // for why it couldn't go (it stays where it is). Not Ok otherwise, saying
    // why in the teacher's words: nothing changes.
    internal async Task<(bool Ok, string Detail)> HandedOverAsync(string name, string senderThumbprint)
    {
        long? own;
        try
        {
            own = ClassHomeFileMaster.Read(_homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            own = null; // the Agents' word is what counts: the sender must be their home
        }

        var homes = await Task.Run(() => Homes(AgentsOnline())).ConfigureAwait(false);
        var newHome = homes.FirstOrDefault(home => home.Name.Length > 0
            && string.Equals(home.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(home.Thumbprint, senderThumbprint, StringComparison.OrdinalIgnoreCase)
            && !home.IsSamePc(_certificate.Thumbprint, _machineName, own)
            && (own is not { } generation || home.Generation > generation));
        if (newHome is null)
        {
            _log($"{name} said it took the class folders over, but no Agent keeps it as the home.");
            return (false, ClassFolderText.NotTheHomeYet(name, _machineName));
        }
        if (_sync.Lesson is { IsOn: true } lesson)
        {
            _log($"{name} took the class folders over with the lesson of {lesson.Turma} on here: kept until it ends.");
            return (false, ClassFolderText.LetsGoAfterTheLesson(_machineName, lesson.Turma));
        }

        await _sync.StandDownAsync().ConfigureAwait(false);
        try
        {
            ClassHomeFileMaster.WriteLastOtherHome(newHome, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The class folders' new home could not be saved as the one seen: {ex.Message}");
        }

        try
        {
            var aside = await Task.Run(() => _store.MoveAside(PassedToWhy(newHome.Name))).ConfigureAwait(false);
            _log($"The class folders went to {newHome.Name}; this PC's copy is now at {aside ?? "(none)"}.");
            return (true, aside is null ? string.Empty : Path.GetFileName(aside));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The class folders went to {newHome.Name}, but this PC's copy could not be put aside: {ex.Message}");
            return (true, NotMovedMark + ErrorText.Describe(ex));
        }
    }

    // A SendFile header, then exactly size bytes of content in FileChunks. False
    // when it stopped: stop asked for it, or the content ended early (a file
    // changed: said to the other side).
    private bool SendOne(SecureChannel channel, string path, Stream content, long size, CancellationToken stop)
    {
        channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(path, size)));
        var buffer = new byte[(int)Math.Min(FileTransfer.ChunkSize, Math.Max(size, 1))];
        long sent = 0;
        while (sent < size)
        {
            if (stop.IsCancellationRequested)
            {
                return false;
            }

            int read;
            try
            {
                read = content.Read(buffer, 0, (int)Math.Min(buffer.Length, size - sent));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                channel.Send(Stopped(ClassFolderRemote.UnreadableText + ErrorText.Describe(ex)));
                return false;
            }
            if (read == 0)
            {
                channel.Send(Stopped(ChangedText));
                return false;
            }

            channel.Send(new Message(MessageType.FileChunk, buffer[..read]));
            sent += read;
        }
        return true;
    }

    private static Message Refusal(string detail) => new(MessageType.CommandAck, CommandResult.Encode(false, detail));

    // A copy stopped in the middle (why: a file changed or can't be read):
    // a lesson started meanwhile, which changes the store, is the reason to give.
    private Message Stopped(string why) => Refusal(_sync.Lesson.IsOn ? ClassFolderText.EndLessonFirst : why);
}
