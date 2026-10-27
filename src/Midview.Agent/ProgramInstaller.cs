using System.ComponentModel;
using System.Diagnostics;
using Midview.Protocol;

namespace Midview.Agent;

// Installs a program the teacher sent, for the console's "Instalar programa"
// (see ProgramInstall): receives the installer into a folder of its own and
// runs it as this SYSTEM service -- no UAC prompt, no administrator password
// typed anywhere -- in its silent mode, with the arguments the teacher's
// Master chose. A Windows Installer package goes through msiexec with /qn;
// any other installer gets the arguments as they came. The folder is under
// %ProgramData%\Midview, which students can read but not write, so the file
// can't be swapped between arriving and running; it's deleted afterwards.
// One install at a time.
//
// The folder, the free-space check and the runner are injected so tests use a
// throwaway folder and a fake runner instead of real installers.
public sealed class ProgramInstaller
{
    // Left free after the installer is in: what it unpacks and installs needs
    // room too.
    private const long DiskFloor = 500L * 1024 * 1024;

    private readonly string _folder;
    private readonly Func<string, long> _freeBytes;
    private readonly Func<string, string, TimeSpan, int?> _run;
    private int _busy;

    // run: starts a program (file name, arguments) and waits up to the
    // timeout; the exit code, or null when it didn't end in time (then it's
    // been ended). Throws when it can't start.
    public ProgramInstaller(string folder, Func<string, long> freeBytes, Func<string, string, TimeSpan, int?> run)
    {
        _folder = folder;
        _freeBytes = freeBytes;
        _run = run;
        DeleteLeftovers();
    }

    public static string DefaultFolder => Path.Combine(AgentDataDirectory.DirectoryPath, "instalar");

    // The whole exchange on one connection: the request's answer, the chunks,
    // InstallStarted, and the installer's result once it ends.
    public void Handle(byte[] requestPayload, Func<Message> receive, Action<Message> send)
    {
        if (!ProgramInstall.TryDecodeRequest(requestPayload, out var request))
        {
            send(Result(false, "pedido inválido"));
            return;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            send(Result(false, "outra instalação já está em andamento nesta máquina"));
            return;
        }

        string? attemptFolder = null;
        var answered = false;
        try
        {
            Directory.CreateDirectory(_folder);
            var free = _freeBytes(_folder);
            if (free < request.Size + DiskFloor)
            {
                send(Result(false, $"pouco espaço em disco nesta máquina (livre: {free / (1024 * 1024)} MB)"));
                return;
            }

            attemptFolder = Path.Combine(_folder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attemptFolder);
            var installerPath = Path.Combine(attemptFolder, request.FileName);

            // Opened before accepting, so a file that can't be written is a
            // plain refusal; closed before running, so the installer can open it.
            string? problem;
            using (var file = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                answered = true;
                send(new Message(MessageType.InstallAccepted, Array.Empty<byte>()));
                problem = ReceiveFile(request.Size, file, receive);
            }
            if (problem is not null)
            {
                send(Result(false, problem));
                return;
            }
            send(new Message(MessageType.InstallStarted, Array.Empty<byte>()));

            var (fileName, arguments) = CommandFor(installerPath, request.Arguments);
            AgentLog.Warn($"Installing {request.FileName}: {fileName} {arguments}");
            (bool Succeeded, string Detail) outcome;
            try
            {
                outcome = Outcome(_run(fileName, arguments, ProgramInstall.InstallTimeout));
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                outcome = (false, $"o instalador não pôde ser iniciado: {ErrorText.Describe(ex)}");
            }
            AgentLog.Warn($"Installing {request.FileName}: {outcome.Detail}");
            send(Result(outcome.Succeeded, outcome.Detail));
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Installing {request.FileName} failed: {ex.Message}");
            if (!answered)
            {
                try
                {
                    send(Result(false, $"erro na máquina: {ErrorText.Describe(ex)}"));
                }
                catch (Exception sendEx) when (sendEx is IOException or ObjectDisposedException)
                {
                    // The Master is gone too.
                }
            }
        }
        finally
        {
            if (attemptFolder is not null)
            {
                TryDelete(attemptFolder);
            }
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // A Windows Installer package goes through msiexec, silent and never
    // restarting the machine on its own; the teacher's arguments follow, as
    // extra options (properties like ALLUSERS=1). Any other installer is run
    // itself, with the arguments as they came.
    internal static (string FileName, string Arguments) CommandFor(string installerPath, string arguments)
    {
        if (ProgramInstall.IsMsi(installerPath))
        {
            return ("msiexec.exe", $"/i \"{installerPath}\" /qn /norestart {arguments}".TrimEnd());
        }
        return (installerPath, arguments);
    }

    // What the teacher reads for an installer's exit code. 3010 and 1641 are
    // Windows Installer's (and many EXE installers') "done, restart to finish";
    // the rest of the named ones are Windows Installer's too.
    internal static (bool Succeeded, string Detail) Outcome(int? exitCode) => exitCode switch
    {
        null => (false, $"o instalador não terminou em {ProgramInstall.InstallTimeout.TotalMinutes:0} minutos e foi encerrado: "
                        + "ele pode estar esperando uma resposta (confira as opções do instalador)"),
        0 => (true, "instalado"),
        3010 or 1641 => (true, "instalado; reinicie a máquina para concluir"),
        1602 or 1223 => (false, "a instalação foi cancelada"),
        1618 => (false, "outra instalação do Windows está em andamento nesta máquina; tente de novo daqui a pouco"),
        1638 => (false, "outra versão deste programa já está instalada (código 1638)"),
        1603 => (false, "o instalador falhou (código 1603)"),
        _ => (false, $"o instalador terminou com o código {exitCode}"),
    };

    // Starts the program with no window and waits for it up to timeout; one
    // still running then is ended with everything it started, so an installer
    // stuck on a question nobody can see in session 0 isn't left running as
    // SYSTEM.
    public static int? RunProcess(string fileName, string arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (Path.IsPathRooted(fileName))
        {
            startInfo.WorkingDirectory = Path.GetDirectoryName(fileName)!;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("o Windows não iniciou o programa");
        if (process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            return process.ExitCode;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone, or couldn't be ended; nothing more to do.
        }
        return null;
    }

    private static string? ReceiveFile(long size, FileStream file, Func<Message> receive)
    {
        long received = 0;
        while (received < size)
        {
            var message = receive();
            if (message.Type != MessageType.FileChunk || message.Payload.Length == 0
                || received + message.Payload.Length > size)
            {
                return "transferência inválida";
            }

            try
            {
                file.Write(message.Payload);
            }
            catch (IOException ex)
            {
                return $"o instalador não pôde ser gravado: {ErrorText.Describe(ex)}";
            }
            received += message.Payload.Length;
        }
        file.Flush(flushToDisk: true);
        return null;
    }

    // An installer from an install the machine was switched off during, or
    // whose folder couldn't be deleted then: nothing is running at start.
    private void DeleteLeftovers()
    {
        try
        {
            if (!Directory.Exists(_folder))
            {
                return;
            }
            foreach (var leftover in Directory.EnumerateDirectories(_folder))
            {
                TryDelete(leftover);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't clean up old installers: {ex.Message}");
        }
    }

    // An installer can still hold its own file for a moment after it ends
    // (or a child it started can), so a failed delete is retried at the next
    // start (DeleteLeftovers) rather than failing the install.
    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't delete {folder}: {ex.Message}");
        }
    }

    private static Message Result(bool succeeded, string detail) =>
        new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));
}
