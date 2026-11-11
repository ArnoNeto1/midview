using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Midview.Master;

// A QR the teacher showed whose phone has not yet appeared: its number, the
// one-use token and when the proof stops being valid.
public sealed record PendingPhoneClaim(string Number, string Token, DateTimeOffset ExpiresAt);

// The pending claims, kept between runs of the Master. On disk they exist only
// as a DPAPI CurrentUser blob (a JSON list), like PhoneWifiFile: the tokens
// are proofs of use and must not be readable by another Windows user.
public static class PendingPhoneClaimsFile
{
    // The UI thread (Add) and the watcher on the thread pool (Remove) both
    // read-modify-write the same file: one at a time.
    private static readonly object Gate = new();

    private static string PathIn(string? folder) => Path.Combine(folder ?? MasterData.Folder, "phone-claims.bin");

    // Claims still valid at "now"; expired ones are dropped and the file is
    // rewritten without them. Missing or damaged file: empty list, no throw.
    public static IReadOnlyList<PendingPhoneClaim> LoadValid(DateTimeOffset now, string? folder = null)
    {
        lock (Gate)
        {
            var all = Read(folder);
            var valid = all.Where(c => c.ExpiresAt >= now).ToList();
            if (valid.Count != all.Count)
            {
                TryWrite(valid, folder);
            }
            return valid;
        }
    }

    // A number already pending is replaced by the new claim.
    public static void Add(PendingPhoneClaim claim, string? folder = null)
    {
        lock (Gate)
        {
            var list = Read(folder).Where(c => c.Number != claim.Number).ToList();
            list.Add(claim);
            Write(list, folder);
        }
    }

    public static void Remove(string token, string? folder = null)
    {
        lock (Gate)
        {
            var all = Read(folder);
            var rest = all.Where(c => c.Token != token).ToList();
            if (rest.Count != all.Count)
            {
                Write(rest, folder);
            }
        }
    }

    private static List<PendingPhoneClaim> Read(string? folder)
    {
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(PathIn(folder)), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<PendingPhoneClaim>>(Encoding.UTF8.GetString(plain))
                ?.Where(c => c is not null && c.Number is not null && c.Token is not null).ToList() ?? new();
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    private static void TryWrite(List<PendingPhoneClaim> list, string? folder)
    {
        try
        {
            Write(list, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dropping expired claims is housekeeping; the next call retries.
        }
    }

    // The whole list at once: a temporary file, then a move over the old one.
    private static void Write(List<PendingPhoneClaim> list, string? folder)
    {
        var path = PathIn(folder);
        var blob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list)), optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, blob);
        File.Move(temp, path, overwrite: true);
    }
}
