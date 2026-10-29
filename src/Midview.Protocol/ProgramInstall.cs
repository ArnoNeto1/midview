using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// Installing a program on the lab machines, the console's "Instalar
// programa": the teacher picks an installer on their PC and each Agent runs it
// as its SYSTEM identity, so no UAC prompt comes up and nobody types the
// administrator password. The Master opens with InstallProgram (an
// InstallRequest); the Agent answers InstallAccepted or a
// CommandAck/CommandResult refusal; after an accept come FileChunk messages up
// to exactly the requested size; the Agent answers InstallStarted once it has
// the whole file and, when the installer ends, one CommandAck/CommandResult
// saying how it went. An Agent from before this answers CommandAck [0].
public static class ProgramInstall
{
    // Bigger than a file sent to the students (FileTransfer.MaxFileSize): a
    // whole suite's offline installer runs to more than 1 GB.
    public const long MaxFileSize = 4L << 30;

    public const int MaxArgumentsBytes = 2048;

    // Until the Agent gives up on an installer and ends it: one that waits on
    // a question in session 0, where nobody can see it, never finishes. Long
    // enough for a big suite on a slow lab machine.
    public static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    // How long the Master waits for the answer once the file is there: the
    // Agent's own deadline, ending the installer, and the answer's way back.
    public static readonly TimeSpan MasterWait = InstallTimeout + TimeSpan.FromMinutes(2);

    // What the Agent knows how to run: a Windows Installer package goes
    // through msiexec, anything else must be a program or batch script.
    public static bool IsInstallerName(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".exe" or ".msi" or ".bat" or ".cmd";

    public static bool IsMsi(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".msi", StringComparison.OrdinalIgnoreCase);

    public static bool IsBatchScript(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".bat" or ".cmd";

    // InstallProgram payload: the file's size (int64, big-endian), the
    // arguments' length in bytes (uint16, big-endian), the arguments (UTF-8),
    // then the file name (UTF-8).
    public static byte[] EncodeRequest(InstallRequest request)
    {
        var arguments = Encoding.UTF8.GetBytes(request.Arguments);
        var name = Encoding.UTF8.GetBytes(request.FileName);
        if (arguments.Length > MaxArgumentsBytes || name.Length > FileTransfer.MaxNameBytes)
        {
            throw new ArgumentException("invalid install request", nameof(request));
        }

        var payload = new byte[8 + 2 + arguments.Length + name.Length];
        BinaryPrimitives.WriteInt64BigEndian(payload, request.Size);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)arguments.Length);
        arguments.CopyTo(payload, 10);
        name.CopyTo(payload, 10 + arguments.Length);
        return payload;
    }

    // Rejected rather than repaired: the name must already be a plain,
    // safe file name (FileNames.Safe would leave it unchanged) of an
    // installer, and the arguments one line of text.
    public static bool TryDecodeRequest(byte[] payload, out InstallRequest request)
    {
        request = new InstallRequest(string.Empty, 0, string.Empty);
        if (payload.Length < 10)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadInt64BigEndian(payload);
        int argumentsLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(8));
        var nameLength = payload.Length - 10 - argumentsLength;
        if (size <= 0 || size > MaxFileSize || argumentsLength > MaxArgumentsBytes
            || nameLength <= 0 || nameLength > FileTransfer.MaxNameBytes)
        {
            return false;
        }

        string arguments, name;
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            arguments = strict.GetString(payload, 10, argumentsLength);
            name = strict.GetString(payload, 10 + argumentsLength, nameLength);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (arguments.Any(char.IsControl) || FileNames.Safe(name) != name || !IsInstallerName(name))
        {
            return false;
        }

        request = new InstallRequest(name, size, arguments.Trim());
        return true;
    }
}

public sealed record InstallRequest(string FileName, long Size, string Arguments);
