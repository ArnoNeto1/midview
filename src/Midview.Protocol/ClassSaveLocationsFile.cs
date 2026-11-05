using System.Text.Json;
using Microsoft.Win32;

namespace Midview.Protocol;

// What Midview pointed at a lesson's class folder, and what was there before, so
// the Session (and the Agent, on uninstall) can undo it.
public sealed record ClassSaveLocationsState(Guid LessonId, string Folder, IReadOnlyList<SavedSetting> Settings)
{
    public static readonly ClassSaveLocationsState None =
        new(Guid.Empty, "", Array.Empty<SavedSetting>());
}

public static class ClassSaveLocationsFile
{
    public const string FileName = "class-save-locations.json";

    // The state is a handful of settings, so a bigger file was not written by Midview.
    public const int MaxBytes = 65536;

    // The shapes read from disk: every string may be null, as the student can edit the file.
    private sealed class StoredState
    {
        public Guid LessonId { get; set; }
        public string? Folder { get; set; }
        public List<StoredSetting?>? Settings { get; set; }
    }

    private sealed class StoredSetting
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public RegistryValueKind Kind { get; set; }
        public string? Before { get; set; }
        public string? Written { get; set; }
    }

    // Never throws. A file that is missing is no state and no problem; one that
    // cannot be trusted is no state, with the reason in `problem`.
    // linkFreeBelow: when set, the file and every folder from it up to (not
    // including) that folder must not be links, so a student cannot point the
    // Agent at a file somewhere else.
    public static ClassSaveLocationsState Read(string path, out string? problem, string? linkFreeBelow = null)
    {
        problem = null;
        try
        {
            if (!File.Exists(path))
            {
                if (Directory.Exists(path))
                {
                    problem = $"{path} is a folder.";
                    return ClassSaveLocationsState.None;
                }
                return ClassSaveLocationsState.None;
            }

            if (linkFreeBelow is not null && LinkOnTheWay(path, linkFreeBelow) is { } link)
            {
                problem = $"{link} is a link.";
                return ClassSaveLocationsState.None;
            }

            var data = ReadLimited(path);
            if (data is null)
            {
                problem = $"{path} is bigger than {MaxBytes} bytes.";
                return ClassSaveLocationsState.None;
            }

            var stored = JsonSerializer.Deserialize<StoredState>(data);
            if (stored is null)
            {
                problem = $"{path} holds no state.";
                return ClassSaveLocationsState.None;
            }

            var settings = new List<SavedSetting>();
            foreach (var item in stored.Settings ?? new List<StoredSetting?>())
            {
                if (item?.Key is null || item.Name is null || item.Written is null)
                    continue;
                var setting = new SavedSetting(item.Key, item.Name, item.Kind, item.Before, item.Written);
                if (ClassSaveTargets.IsAllowed(setting))
                    settings.Add(setting);
            }
            return new ClassSaveLocationsState(stored.LessonId, stored.Folder ?? "", settings);
        }
        catch (Exception ex)
        {
            problem = $"Could not read {path}: {ex.Message}";
            return ClassSaveLocationsState.None;
        }
    }

    // Null when the file is bigger than MaxBytes. Reads one byte more than the
    // limit, so a file that grows while it is read is not taken whole.
    private static byte[]? ReadLimited(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[MaxBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total > MaxBytes ? null : buffer.AsSpan(0, total).ToArray();
    }

    // The first link among the file and its folders below `root`, or null.
    private static string? LinkOnTheWay(string path, string root)
    {
        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = Path.GetFullPath(path);
        while (!string.Equals(Path.TrimEndingDirectorySeparator(current), stop, StringComparison.OrdinalIgnoreCase))
        {
            if (ClassFolderRules.IsLink(current))
                return current;
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
            if (string.IsNullOrEmpty(parent))
                break;
            current = parent;
        }
        return null;
    }

    // Written to a temporary file and moved over the old one, as ClassFolder.SaveState
    // does. IO errors reach the caller, which must not go on without the state saved.
    public static void Save(string path, ClassSaveLocationsState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new StoredStateOut(state.LessonId, state.Folder, state.Settings));
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    private sealed record StoredStateOut(Guid LessonId, string Folder, IReadOnlyList<SavedSetting> Settings);
}
