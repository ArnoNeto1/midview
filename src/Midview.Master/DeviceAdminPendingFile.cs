using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The phones and tablets the school's admin password didn't reach when it was
// saved ("Senha de administrador": off, or out of the network), by Agent
// thumbprint: MainWindow puts it on each as soon as it shows up online
// (MainWindow.DeviceAdminArrivals), across a restart of the Master. Saving a
// password again replaces the list; "Remover" empties it. Only thumbprints,
// so a plain file: the password itself stays in PhoneDefaultsFile.
public static class DeviceAdminPendingFile
{
    private static string PathIn(string? folder) => Path.Combine(folder ?? MasterData.Folder, "phone-admin-pending.json");

    // Empty when nothing waits or the file can't be read.
    public static HashSet<string> Load(string? folder = null)
    {
        try
        {
            var agents = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PathIn(folder))) ?? new();
            return Normalized(agents);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public static void Save(IEnumerable<string> agents, string? folder = null)
    {
        var path = PathIn(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Normalized(agents).OrderBy(agent => agent, StringComparer.Ordinal)));
        File.Move(temporary, path, overwrite: true);
    }

    // This device got it: off the list (the file left alone when it wasn't on it).
    public static void Remove(string agent, string? folder = null)
    {
        var agents = Load(folder);
        if (agents.Remove(agent.Trim().ToUpperInvariant()))
        {
            Save(agents, folder);
        }
    }

    private static HashSet<string> Normalized(IEnumerable<string> agents) =>
        agents.Where(agent => !string.IsNullOrWhiteSpace(agent))
            .Select(agent => agent.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
}

// One device of DeviceAdminPendingFile that showed up online: what sending it
// the password came to, and whether it should go on waiting for it.
public static class DeviceAdminArrival
{
    public enum Outcome
    {
        Put,
        Refused,
        TooOld,
        NoAnswer,
    }

    // setAdmin: PhoneNetworkClient.SetAdminPassword for the device.
    public static Outcome Put(Func<(bool Succeeded, string Detail)?> setAdmin)
    {
        try
        {
            return setAdmin() switch
            {
                { Succeeded: true } => Outcome.Put,
                { } => Outcome.Refused,
                null => Outcome.TooOld,
            };
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException
                                       or System.Security.Authentication.AuthenticationException
                                       or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            return Outcome.NoAnswer;
        }
    }

    // Only a device that didn't answer is tried again: one that answered no,
    // or is too old, would answer the same every time.
    public static bool KeepsWaiting(Outcome outcome) => outcome == Outcome.NoAnswer;
}
