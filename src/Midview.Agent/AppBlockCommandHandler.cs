using System.Globalization;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Agent;

// The program-blocking policy on this machine (MessageType.SetAppBlock and
// GetAppBlock): kept on disk until the teacher lifts it, handed to the
// enforcer. The newest SavedAt wins, as with the power schedule, so two
// teachers' Masters don't keep overwriting each other's.
public sealed class AppBlockCommandHandler
{
    private readonly string _statePath;
    private readonly Action<AppBlockPolicy> _apply;
    private readonly Func<string?> _warning;
    private readonly object _gate = new();
    private AppBlockPolicy _policy;

    // warning: something that stops the block from working as the teacher
    // expects on this machine, said in the answer when a block is applied.
    public AppBlockCommandHandler(string statePath, Action<AppBlockPolicy> apply, Func<string?>? warning = null)
    {
        _statePath = statePath;
        _apply = apply;
        _warning = warning ?? (() => null);
        _policy = Load(statePath) ?? AppBlockPolicy.Lifted(0);
    }

    public static string DefaultStatePath => Path.Combine(AgentDataDirectory.DirectoryPath, "app-block.json");

    public AppBlockPolicy Current
    {
        get => Volatile.Read(ref _policy);
    }

    public (bool Succeeded, string Detail) Handle(byte[] payload)
    {
        if (!AppBlockPolicy.TryDecode(payload, out var policy))
        {
            return (false, "pedido inválido");
        }

        lock (_gate)
        {
            if (policy.SavedAt < _policy.SavedAt)
            {
                return (true, string.Create(CultureInfo.InvariantCulture,
                    $"mantido o bloqueio mais novo, salvo em {SavedAtText(_policy.SavedAt)}: {Describe(_policy)}"));
            }
            if (policy.SavedAt == _policy.SavedAt)
            {
                return policy.SameRulesAs(_policy)
                    ? (true, Describe(_policy))
                    : (false, "outro bloqueio foi salvo no mesmo segundo; aplique de novo");
            }

            try
            {
                Save(_statePath, policy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLog.Warn($"Couldn't save the program block: {ex.Message}");
                return (false, $"não foi possível gravar o bloqueio: {ex.Message}");
            }

            Volatile.Write(ref _policy, policy);
            _apply(policy);
            return (true, policy.IsActive && _warning() is { } warning ? $"{Describe(policy)} (atenção: {warning})" : Describe(policy));
        }
    }

    public static string Describe(AppBlockPolicy policy) => policy.Mode switch
    {
        AppBlockMode.BlockList => $"bloqueando {policy.Programs.Count} programa(s)",
        AppBlockMode.AllowOnly => policy.Programs.Count == 0
            ? "permitindo só o Windows"
            : $"permitindo só o Windows e {policy.Programs.Count} programa(s)",
        _ => "nenhum programa bloqueado",
    };

    private static string SavedAtText(long savedAt) =>
        DateTimeOffset.FromUnixTimeSeconds(savedAt).ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    // Unreadable means nothing blocked: the side the teacher can always fix
    // remotely. The file is kept aside, as with SiteBlockState.
    private static AppBlockPolicy? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        AppBlockPolicy? policy;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPolicy>(File.ReadAllText(path));
            policy = stored is null ? null : new AppBlockPolicy(stored.SavedAt, stored.Mode, stored.Programs ?? new List<string>());
            if (policy is null || !policy.IsValid())
            {
                throw new JsonException("invalid program block");
            }
            return policy;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // The content is bad: kept aside, so it isn't read (and fails) at every start.
            AgentLog.Warn($"Couldn't read the program block, blocking nothing: {ex.Message}");
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Left in place; it's read again (and fails again) next start.
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Couldn't read it (locked, antivirus, permissions): the file may be fine, so it
            // stays where it is and is read again at the next start.
            AgentLog.Warn($"Couldn't read the program block file, blocking nothing: {ex.Message}");
            return null;
        }
    }

    private static void Save(string path, AppBlockPolicy policy)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new StoredPolicy(policy.SavedAt, policy.Mode, policy.Programs.ToList()));
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record StoredPolicy(long SavedAt, AppBlockMode Mode, List<string>? Programs);
}
