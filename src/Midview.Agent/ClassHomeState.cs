using System.Net;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Agent;

// Which teacher PC keeps the class folders ("Pasta da turma" with two teacher
// PCs in one lab, MessageType.ClaimClassHome and GetClassHome): the one whose
// Master claimed it first, or, for a Master that sends no claim, started the
// first lesson. Only the home may start a lesson or change the class; any
// trusted teacher may end one. ClassLessonHandler asks under its lesson's
// lock, so a claim and a start never cross.
//
// The identity is the thumbprint (from the TLS channel, never the payload)
// and the computer name (from the claim): two teacher PCs can share one
// certificate. The home's own name with another certificate is still the
// home (Midview reinstalled or paired again, or another Windows account on
// that PC: each has its own teacher identity), and so is its certificate with
// its generation under a new name (the PC renamed). The generation (ClassHome.
// Generation) is when that PC became the home: another PC takes the folders
// over only with a newer one. The address is where the connection came from,
// refreshed by every claim from the home, so a home whose address changed is
// found again.
//
// Kept in %ProgramData%\Midview\class-home.json, replaced whole so a crash
// never leaves half of it; a corrupt file is set aside and means no home.
public sealed class ClassHomeState
{
    public const string HomeIsHereText = "este PC guarda as pastas";
    public const string MovedHereText = "as pastas passaram para este PC";

    // The folders don't move while a lesson is on (plan decision 7).
    public const string EndLessonFirstText = "Encerre a aula antes de passar as pastas.";

    // In a refusal, for a home recorded with no name (a Master that sent no
    // claim): "As pastas das turmas ficam no PC de outro professor".
    public const string UnnamedHomeText = ClassHome.UnnamedText;

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

    // senderThumbprint: the TLS channel's; senderAddress: the connection's.
    // lessonOn: whether this machine follows a lesson now (a TakeOver waits
    // for its end).
    //
    // The home's own PC (ClassHome.IsSamePc: its identity, its certificate and
    // generation under a new name, or its name under a new certificate)
    // refreshes what is recorded and keeps the newer generation. Another PC is
    // refused, naming the home, unless it takes the folders over with a newer
    // generation than the home's (plan Ruling 9: an old home can't undo a
    // takeover) and no lesson is on.
    public (bool Ok, string Detail) Claim(string senderThumbprint, string senderAddress, ClassHomeClaim claim, bool lessonOn)
    {
        if (!IsSender(senderThumbprint, senderAddress) || claim.Name.Length == 0 || !IsUsable(claim.Name, claim.Port)
            || claim.Generation <= 0)
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
                claimed = new ClassHome(Normalize(senderThumbprint), claim.Name, senderAddress, claim.Port, claim.Generation);
                detail = HomeIsHereText;
            }
            else if (home.IsSamePc(senderThumbprint, claim.Name, claim.Generation))
            {
                // The name a Master with no claim left empty is filled in, and a
                // renamed PC's new name taken; the same name in other letters stays.
                var name = string.Equals(home.Name, claim.Name, StringComparison.OrdinalIgnoreCase) ? home.Name : claim.Name;
                claimed = new ClassHome(
                    Normalize(senderThumbprint), name, senderAddress, claim.Port, Math.Max(home.Generation, claim.Generation));
                detail = HomeIsHereText;
            }
            else if (!claim.TakeOver || claim.Generation <= home.Generation)
            {
                return (false, home.RefusalText);
            }
            else if (lessonOn)
            {
                return (false, EndLessonFirstText);
            }
            else
            {
                claimed = new ClassHome(Normalize(senderThumbprint), claim.Name, senderAddress, claim.Port, claim.Generation);
                detail = MovedHereText;
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

    // May this teacher start a lesson or change the class? With no home, yes,
    // and it becomes the home with no name (a Master that sent no claim,
    // decision 4; a sender with no identity isn't recorded). With one, only
    // from the home's certificate: the name was checked at the claim, and a
    // Master refused there doesn't go on to start. refusal: the answer when
    // not, naming the home.
    public bool TryStart(string senderThumbprint, string senderAddress, out string refusal)
    {
        refusal = "";
        lock (_gate)
        {
            var home = _home;
            if (home is null)
            {
                RecordStarter(senderThumbprint, senderAddress);
                return true;
            }
            if (string.Equals(home.Thumbprint, senderThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            refusal = home.RefusalText;
            return false;
        }
    }

    // Kept for this run even if it can't be saved, so the rest of today's
    // lessons go by it. Under _gate, with no home.
    private void RecordStarter(string senderThumbprint, string senderAddress)
    {
        if (!IsSender(senderThumbprint, senderAddress))
        {
            return;
        }

        var starter = new ClassHome(Normalize(senderThumbprint), "", senderAddress, ClassFolderRemote.Port, 0);
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
            JsonSerializer.Serialize(stream, new StoredHome(home.Thumbprint, home.Name, home.Address, home.Port, home.Generation));
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
            && IsUsable(stored.Name, stored.Port)
            && stored.Generation >= 0)
        {
            return new ClassHome(Normalize(stored.Thumbprint), stored.Name, stored.Address, stored.Port, stored.Generation);
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

    // Generation: 0 in a file written before it (a home with no claim's generation).
    private sealed record StoredHome(string Thumbprint, string Name, string Address, int Port, long Generation);
}
