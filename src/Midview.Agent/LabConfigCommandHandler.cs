using System.Globalization;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Agent;

// The lab-configuration panel on this machine (MessageType.SetLabConfig,
// GetLabConfig, ResetLabConfig). The desired state is kept on disk until the
// teacher changes it, written to the registry by LabPolicyWriter and, for
// "Bloquear Edge", handed to the program-blocking enforcer. The newest
// SavedAt wins, as with the power schedule and program blocking, so two
// teachers' Masters don't keep overwriting each other.
public sealed class LabConfigCommandHandler
{
    // The notice shown in the student's session when the browser config is
    // applied and Chrome is closed so it takes effect. The class folder's
    // download folder closes Chrome with it too (ClassLessonHandler).
    internal const string ChromeClosedNotice =
        "O professor atualizou as configurações do navegador; o Chrome foi fechado para aplicar. Pode abri-lo de novo.";

    // The notice shown when the saved Chrome profiles are cleared.
    private const string ChromeClearedNotice =
        "O professor limpou os perfis salvos do Chrome; ele foi fechado e abrirá zerado.";

    private readonly string _statePath;
    private readonly LabPolicyWriter _writer;
    private readonly AppBlockEnforcer _enforcer;
    private readonly string _usersRoot;
    private readonly object _gate = new();
    private LabConfig _config;

    public LabConfigCommandHandler(string statePath, LabPolicyWriter writer, AppBlockEnforcer enforcer, string? usersRoot = null)
    {
        _statePath = statePath;
        _writer = writer;
        _enforcer = enforcer;
        _usersRoot = usersRoot ?? Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Users");
        _config = Load(statePath) ?? LabConfig.Off(0);
    }

    public static string DefaultStatePath => Path.Combine(AgentDataDirectory.DirectoryPath, "lab-config.json");

    // What the Agent keeps applying on this machine (GetLabConfig). The
    // Agent only answered a SetLabConfig with success once the registry write
    // itself succeeded as SYSTEM, so this is also what is in force; it is
    // reapplied at every boot.
    public LabConfig Current => Volatile.Read(ref _config);

    public (bool Succeeded, string Detail) Handle(byte[] payload)
    {
        if (!LabConfig.TryDecode(payload, out var config))
        {
            return (false, "pedido inválido");
        }

        lock (_gate)
        {
            if (config.SavedAt < _config.SavedAt)
            {
                return (true, string.Create(CultureInfo.InvariantCulture,
                    $"mantida a configuração mais nova, salva em {SavedAtText(_config.SavedAt)}: {Describe(_config)}"));
            }
            if (config.SavedAt == _config.SavedAt)
            {
                return config.SameLockdownsAs(_config)
                    ? (true, Describe(_config))
                    : (false, "outra configuração foi salva no mesmo segundo; aplique de novo");
            }

            // RESET is its own command (ResetLabConfig); turning the last
            // function off here only removes the functions' own keys.
            return ApplyAndSave(config, wipe: false);
        }
    }

    public (bool Succeeded, string Detail) Reset()
    {
        lock (_gate)
        {
            // A reset always wins: it carries the current second, so a later
            // SetLabConfig can still take over.
            var off = LabConfig.Off(Math.Max(_config.SavedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            return ApplyAndSave(off, wipe: true);
        }
    }

    // "Limpar perfis do Chrome": closes the student's Chrome and deletes
    // every user's saved profile, so the browser opens blank for the next
    // class. Not part of the saved state -- it is a one-off action, not a
    // lockdown that reapplies at boot. Best-effort and never throws.
    public (bool Succeeded, string Detail) ClearChromeProfiles()
    {
        _enforcer.EndNow("chrome.exe", ChromeClearedNotice);

        var cleared = 0;
        var failed = 0;
        try
        {
            if (Directory.Exists(_usersRoot))
            {
                // The chain from the profile root down to "User Data": each
                // segment must be a real directory, never a reparse point.
                var chain = new[] { "AppData", "Local", "Google", "Chrome", "User Data" };
                foreach (var userDirectory in Directory.GetDirectories(_usersRoot))
                {
                    // The Agent runs as SYSTEM and these folders are the
                    // student's own, so a junction they plant inside their
                    // profile must not redirect this recursive delete to
                    // somewhere else. Verify every segment opens as a real
                    // directory (not a link) before deleting.
                    var userData = userDirectory;
                    var safe = true;
                    foreach (var segment in chain)
                    {
                        userData = Path.Combine(userData, segment);
                        if (!Directory.Exists(userData))
                        {
                            safe = false; // nothing to clear for this account
                            break;
                        }
                        if (!ProtectedStudentFolders.IsRealDirectory(userData))
                        {
                            // A link (junction/symlink) where a real folder
                            // should be: refuse to follow it as SYSTEM. Not a
                            // delete failure -- there is nothing legitimate to
                            // clear for this account -- just logged.
                            safe = false;
                            AgentLog.Warn($"Skipped a Chrome profile under {userDirectory}: {segment} is a link, not a real folder.");
                            break;
                        }
                    }
                    if (!safe)
                    {
                        continue;
                    }

                    try
                    {
                        Directory.Delete(userData, recursive: true);
                        cleared++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // A file still open (Chrome didn't fully close yet) or
                        // a permission quirk on one account: skip it, keep going.
                        failed++;
                        AgentLog.Warn($"Couldn't clear a Chrome profile under {userDirectory}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't list user profiles to clear Chrome: {ex.Message}");
            return (false, $"não foi possível limpar os perfis: {ErrorText.Describe(ex)}");
        }

        return failed > 0
            ? (cleared > 0, $"perfis do Chrome limpos em {cleared} conta(s); {failed} não deu para apagar agora")
            : (true, cleared == 0 ? "nenhum perfil do Chrome para limpar" : $"perfis do Chrome limpos em {cleared} conta(s)");
    }

    // Deletes the students' downloads: each user's Downloads folder, either
    // everything in it or only today's files (DownloadsScope). As SYSTEM over
    // the student's own folders, so the same link check as ClearChromeProfiles
    // keeps it from following a junction out of the folder. Never throws.
    public (bool Succeeded, string Detail) ClearDownloads(DownloadsScope scope)
    {
        var today = DateTime.Now.Date;
        var cleared = 0;
        var failed = 0;
        try
        {
            if (Directory.Exists(_usersRoot))
            {
                foreach (var userDirectory in Directory.GetDirectories(_usersRoot))
                {
                    if (!ProtectedStudentFolders.IsRealDirectory(userDirectory))
                    {
                        continue;
                    }
                    var downloads = Path.Combine(userDirectory, "Downloads");
                    if (!Directory.Exists(downloads))
                    {
                        continue;
                    }
                    if (!ProtectedStudentFolders.IsRealDirectory(downloads))
                    {
                        AgentLog.Warn($"Skipped Downloads under {userDirectory}: it is a link, not a real folder.");
                        continue;
                    }
                    try
                    {
                        cleared += DeleteDownloads(downloads, scope, today, ref failed);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // One account's folder going unreadable mid-way mustn't
                        // stop the others.
                        failed++;
                        AgentLog.Warn($"Couldn't clear Downloads under {userDirectory}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't list user profiles to clear downloads: {ex.Message}");
            return (false, $"não foi possível limpar os downloads: {ErrorText.Describe(ex)}");
        }

        var what = scope == DownloadsScope.Today ? "downloads de hoje" : "downloads";
        if (failed > 0)
        {
            return (cleared > 0, $"{what}: {cleared} item(ns) apagado(s); {failed} não deu para apagar agora");
        }
        return (true, cleared == 0 ? $"nenhum {what} para apagar" : $"{what}: {cleared} item(ns) apagado(s)");
    }

    // The entries deleted from one Downloads folder. Downloads was already
    // checked to be a real folder.
    private static int DeleteDownloads(string downloads, DownloadsScope scope, DateTime today, ref int failed)
    {
        if (scope == DownloadsScope.All)
        {
            var deleted = 0;
            foreach (var entry in Directory.GetFileSystemEntries(downloads))
            {
                deleted += TryDelete(entry, Directory.Exists(entry), ref failed) ? 1 : 0;
            }
            return deleted;
        }

        return DeleteTodaysFiles(downloads, today, ref failed);
    }

    // "De hoje": only the files whose own date is today, however deep they
    // sit, and any subfolder left empty because it held only today's files.
    // A subfolder that is a link is never followed (it isn't a real folder the
    // student downloaded into), so a planted junction can't redirect this.
    private static int DeleteTodaysFiles(string folder, DateTime today, ref int failed)
    {
        var deleted = 0;
        foreach (var file in Directory.GetFiles(folder))
        {
            if (File.GetLastWriteTime(file).Date == today)
            {
                deleted += TryDelete(file, isDirectory: false, ref failed) ? 1 : 0;
            }
        }
        foreach (var sub in Directory.GetDirectories(folder))
        {
            if (!ProtectedStudentFolders.IsRealDirectory(sub))
            {
                continue;
            }
            deleted += DeleteTodaysFiles(sub, today, ref failed);
            try
            {
                if (Directory.GetFileSystemEntries(sub).Length == 0)
                {
                    Directory.Delete(sub, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leaving an empty subfolder is harmless; don't count it.
            }
        }
        return deleted;
    }

    private static bool TryDelete(string path, bool isDirectory, ref int failed)
    {
        try
        {
            if (isDirectory)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed++;
            AgentLog.Warn($"Couldn't delete a download ({path}): {ex.Message}");
            return false;
        }
    }

    public void ReconcileAtStartup()
    {
        lock (_gate)
        {
            // Declarative, so this just makes the machine match the saved
            // state; the student's Chrome isn't closed at boot.
            _writer.Apply(_config);
            ApplyEdge(_config);
        }
    }

    private (bool Succeeded, string Detail) ApplyAndSave(LabConfig config, bool wipe)
    {
        try
        {
            if (wipe)
            {
                _writer.ResetAll();
            }
            else
            {
                _writer.Apply(config);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AgentLog.Warn($"Couldn't write the lab configuration: {ex.Message}");
            return (false, $"não foi possível gravar a configuração: {ErrorText.Describe(ex)}");
        }

        try
        {
            Save(_statePath, config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't save the lab configuration: {ex.Message}");
            return (false, $"não foi possível gravar a configuração: {ErrorText.Describe(ex)}");
        }

        Volatile.Write(ref _config, config);
        ApplyEdge(config);
        _enforcer.EndNow("chrome.exe", ChromeClosedNotice);
        return (true, Describe(config));
    }

    private void ApplyEdge(LabConfig config) =>
        _enforcer.SetAlwaysEnd(config.BlockEdge
            ? new HashSet<string>(StringComparer.Ordinal) { "msedge.exe" }
            : new HashSet<string>(StringComparer.Ordinal));

    public static string Describe(LabConfig config)
    {
        if (!config.IsAnyOn)
        {
            return "nenhuma configuração aplicada";
        }

        var on = new List<string>();
        if (config.CleanOnExit) on.Add("limpeza ao fechar");
        if (config.Lockdown) on.Add("blindagem");
        if (config.BlockDownloads) on.Add("bloquear downloads");
        if (config.ForceGuest) on.Add("modo visitante");
        if (config.Adblock) on.Add("bloqueador de anúncios");
        if (config.BlockEdge) on.Add("bloquear Edge");
        if (config.LockTaskbar) on.Add("travar barra de tarefas");
        return string.Join(", ", on);
    }

    private static string SavedAtText(long savedAt) =>
        DateTimeOffset.FromUnixTimeSeconds(savedAt).ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    // Unreadable means nothing applied: the side the teacher can always fix
    // remotely. The file is kept aside, as with the program block.
    private static LabConfig? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredConfig>(File.ReadAllText(path));
            return stored is null
                ? null
                : new LabConfig(stored.SavedAt, stored.CleanOnExit, stored.Lockdown, stored.BlockDownloads,
                    stored.ForceGuest, stored.Adblock, stored.BlockEdge, stored.LockTaskbar);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            AgentLog.Warn($"Couldn't read the lab configuration, applying nothing: {ex.Message}");
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Left in place; read again (and failing again) next start.
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't read the lab configuration file, applying nothing: {ex.Message}");
            return null;
        }
    }

    private static void Save(string path, LabConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new StoredConfig(
                config.SavedAt, config.CleanOnExit, config.Lockdown, config.BlockDownloads,
                config.ForceGuest, config.Adblock, config.BlockEdge, config.LockTaskbar));
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record StoredConfig(
        long SavedAt, bool CleanOnExit, bool Lockdown, bool BlockDownloads, bool ForceGuest, bool Adblock, bool BlockEdge,
        bool LockTaskbar);
}
