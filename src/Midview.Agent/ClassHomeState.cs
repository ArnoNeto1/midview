using System.Net;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Agent;

// Which teacher PC keeps the class folders ("Pasta da turma" with two teacher
// PCs in one lab, MessageType.ClaimClassHome and GetClassHome): the one whose
// Master claimed it first, or, for a Master that sends no claim, started the
// first lesson. Only the home may start a lesson or change the class
// (ClassLessonHandler); any trusted teacher may end one.
//
// The identity is the thumbprint (from the TLS channel, never the payload)
// and the computer name (from the claim): two teacher PCs can share one
// certificate. The address is where the connection came from, refreshed by
// every claim from the home, so a home whose address changed is found again.
//
// Kept in %ProgramData%\Midview\class-home.json, replaced whole so a crash
// never leaves half of it; a corrupt file is set aside and means no home.
public sealed class ClassHomeState
{
    public const string HomeIsHereText = "este PC guarda as pastas";
    public const string MovedHereText = "as pastas passaram para este PC";

    // In a refusal, for a home recorded with no name (a Master that sent no
    // claim).
    public const string UnnamedHomeText = "outro PC de professor";

    private readonly string _statePath;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private ClassHome? _home;

    public ClassHomeState(string statePath, Action<string>? log = null)
    {
        _statePath = statePath;
        _log = log ?? AgentLog.Warn;
        _home = Load(statePath);
    }

    public static string DefaultStatePath => Path.Combine(AgentDataDirectory.DirectoryPath, "class-home.json");

    // What GetClassHome answers: null until a teacher PC claims it.
    public ClassHome? Current => Volatile.Read(ref _home);

    // What an Agent answers a claim, or a lesson start, from a PC that isn't
    // the home. Starts with ClassHomeClaim.OtherHomeText, which the Master
    // looks for.
    public static string OtherHomeDetail(ClassHome home) =>
        ClassHomeClaim.OtherHomeText + (home.Name.Length > 0 ? home.Name : UnnamedHomeText);

    // senderThumbprint: the TLS channel's; senderAddress: the connection's.
    public (bool Ok, string Detail) Claim(string senderThumbprint, string senderAddress, ClassHomeClaim claim)
    {
        if (!IsSender(senderThumbprint, senderAddress) || !IsUsable(claim.Name, claim.Port))
        {
            return (false, "pedido inválido");
        }

        lock (_gate)
        {
            var home = _home;
            ClassHome claimed;
            string detail;
            if (home is null)
            {
                claimed = new ClassHome(Normalize(senderThumbprint), claim.Name, senderAddress, claim.Port);
                detail = HomeIsHereText;
            }
            else if (home.IsSameIdentity(senderThumbprint, claim.Name))
            {
                // The name a Master with no claim left empty is filled in.
                claimed = home with
                {
                    Name = home.Name.Length > 0 ? home.Name : claim.Name,
                    Address = senderAddress,
                    Port = claim.Port,
                };
                detail = HomeIsHereText;
            }
            else if (claim.TakeOver)
            {
                claimed = new ClassHome(Normalize(senderThumbprint), claim.Name, senderAddress, claim.Port);
                detail = MovedHereText;
            }
            else
            {
                return (false, OtherHomeDetail(home));
            }

            if (claimed == home)
            {
                return (true, detail);
            }
            try
            {
                Save(claimed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"Couldn't save the class folders' home: {ex.Message}");
                return (false, $"não foi possível gravar o PC das pastas: {ErrorText.Describe(ex)}");
            }
            return (true, detail);
        }
    }

    // A lesson started by a Master that sent no claim (decision 4): it is
    // the home, with no name, while there is none. Kept for this run even if
    // it can't be saved, so the rest of today's lessons go by it.
    public void RecordStarter(string senderThumbprint, string senderAddress)
    {
        if (!IsSender(senderThumbprint, senderAddress))
        {
            return;
        }

        lock (_gate)
        {
            if (_home is not null)
            {
                return;
            }

            var starter = new ClassHome(Normalize(senderThumbprint), "", senderAddress, ClassFolderRemote.Port);
            try
            {
                Save(starter);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"Couldn't save the class folders' home: {ex.Message}");
                Volatile.Write(ref _home, starter);
            }
        }
    }

    // May this teacher start a lesson or change the class? With no home,
    // anyone. The name was checked at the claim: a Master that was refused
    // there doesn't go on to start the lesson.
    public bool MayStart(string senderThumbprint) =>
        Current is not { } home || string.Equals(home.Thumbprint, senderThumbprint, StringComparison.OrdinalIgnoreCase);

    // Written, then made the current home. Throws if it can't be written.
    private void Save(ClassHome home)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _statePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new StoredHome(home.Thumbprint, home.Name, home.Address, home.Port));
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, _statePath, overwrite: true);

        Volatile.Write(ref _home, home);
        _log($"Class folders' home: {(home.Name.Length > 0 ? home.Name : "(no name)")} at {home.Address} ({home.Thumbprint}).");
    }

    private static string Normalize(string thumbprint) => thumbprint.ToUpperInvariant();

    private static bool IsSender(string thumbprint, string address) =>
        TeacherInvite.IsThumbprint(thumbprint)
        && address is { Length: > 0 and <= ClassHome.MaxAddressLength }
        && IPAddress.TryParse(address, out _);

    private static bool IsUsable(string? name, int port) =>
        name is { Length: <= ClassHome.MaxNameLength } && port is >= 1 and <= ushort.MaxValue;

    // Unreadable means no home: every teacher PC may start a lesson, as with
    // one teacher PC, and the next start records one again. A corrupt file is
    // set aside; one that can't be opened right now is left where it is.
    private ClassHome? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        StoredHome? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredHome>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"Couldn't read the class folders' home, following none: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            stored = null;
        }

        if (stored is not null
            && IsSender(stored.Thumbprint, stored.Address)
            && IsUsable(stored.Name, stored.Port))
        {
            return new ClassHome(Normalize(stored.Thumbprint), stored.Name, stored.Address, stored.Port);
        }

        _log("The class folders' home file is corrupt, following none; it is kept aside as .corrupt.");
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place; read again (and failing again) next start.
        }
        return null;
    }

    private sealed record StoredHome(string Thumbprint, string Name, string Address, int Port);
}
