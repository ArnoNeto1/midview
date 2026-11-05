using System.Globalization;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Session;

// ClassSaveLocations (see docs/superpowers/specs/2026-11-04-class-folder-save-locations-design.md):
// while a lesson is on, the student's Downloads, Office's default save folder and
// Notepad's and Paint's "last folder used" point at the lesson's class folder on
// the Desktop; when the lesson ends they are put back. It runs as the student, in
// the Session, and writes only the registry values ClassSaveTargets allows.
//
// What was there before is saved in the state file *before* anything is written,
// so a crash half-way can still be undone (by ClassSaveLocationsUndo, here or in
// the Agent on uninstall). Each target is applied once per lesson: a value the
// student changes afterwards stays theirs, and the undo leaves it alone (a
// Notepad or Paint entry saved again into the class folder is not a change).
public sealed class ClassSaveLocations
{
    private readonly string _desktopDirectory;
    private readonly Func<ClassLesson?> _agentLesson;
    private readonly string _statePath;
    private readonly RegistryKey _software;
    private readonly IShellFolders _shell;
    private readonly Action<string> _log;

    private const string DefaultDownloads = @"%USERPROFILE%\Downloads";

    private readonly object _followLock = new();
    private readonly HashSet<string> _problems = new();
    private ClassSaveLocationsState? _state; // read from the state file on the first Follow

    // desktopDirectory: the student's Desktop ("" when unknown). agentLesson: the
    // lesson the Agent says is on; null is "not known" and Follow does nothing
    // then. software: the student's HKCU\Software (a throwaway key in tests).
    // log: where problems go, the console unless a test listens.
    public ClassSaveLocations(
        string desktopDirectory,
        Func<ClassLesson?> agentLesson,
        string statePath,
        RegistryKey software,
        IShellFolders shell,
        Action<string>? log = null)
    {
        _desktopDirectory = desktopDirectory;
        _agentLesson = agentLesson;
        _statePath = statePath;
        _software = software;
        _shell = shell;
        _log = log ?? Console.WriteLine;
    }

    // Called every few seconds. Undoes a lesson that is over (or replaced), then
    // applies the one that is on if its folder is there. Never throws, and two
    // overlapping calls never both run.
    public void Follow()
    {
        if (!Monitor.TryEnter(_followLock))
        {
            return; // the last call is still writing; it will see the same
        }

        try
        {
            FollowLesson();
        }
        catch (Exception ex)
        {
            Problem($"Following the class save locations failed: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_followLock);
        }
    }

    private void FollowLesson()
    {
        // Not known (the lesson file couldn't be read, say) is never "the lesson is over".
        var reported = _agentLesson();
        if (reported is null)
        {
            return;
        }

        var lesson = reported.IsOn ? reported : null;
        var state = _state ??= ReadState();

        if (state.LessonId != Guid.Empty && (lesson is null || state.LessonId != lesson.LessonId))
        {
            Undo(state);
            // None in memory whatever the file says, so it isn't undone twice.
            state = _state = ClassSaveLocationsState.None;
            SaveState(state);
        }

        if (lesson is not null && state.LessonId != lesson.LessonId)
        {
            Apply(lesson);
        }
    }

    private ClassSaveLocationsState ReadState()
    {
        var state = ClassSaveLocationsFile.Read(_statePath, out var problem);
        if (problem is not null)
        {
            Problem(problem);
        }
        return state;
    }

    private void SaveState(ClassSaveLocationsState state)
    {
        try
        {
            ClassSaveLocationsFile.Save(_statePath, state);
        }
        catch (Exception ex)
        {
            Problem($"Could not remember the class save locations in {_statePath}: {ex.Message}");
        }
    }

    private void Undo(ClassSaveLocationsState state)
    {
        var settings = state.Settings.Select(setting => AsWrittenIfStillTheFolder(setting, state.Folder)).ToList();
        // The shell is told the expanded path; Run writes the raw text afterwards.
        var problems = ClassSaveLocationsUndo.Run(_software, settings,
            before => _shell.SetDownloads(Environment.ExpandEnvironmentVariables(before)));
        foreach (var problem in problems)
        {
            Problem(problem);
        }
    }

    // Saving once in Notepad or Paint rewrites the program's entry with other
    // bytes (an ID list carries timestamps), though it still leads to the class
    // folder. Such an entry is taken for the one Midview wrote, so the shared
    // rule puts the one from before back. Only the Session can decode an ID list
    // (it needs the shell, as the student); the Agent's undo stays byte for byte.
    private SavedSetting AsWrittenIfStillTheFolder(SavedSetting setting, string folder)
    {
        if (!Is(setting, ClassSaveTargets.MruKey))
        {
            return setting;
        }

        try
        {
            var current = ClassSaveTargets.ReadEncoded(_software, setting.Key, setting.Name, out var kind);
            if (current is null || kind != RegistryValueKind.Binary || current == setting.Written)
            {
                return setting;
            }

            var entry = Convert.FromBase64String(current);
            var program = LastVisitedMru.ProgramOf(entry);
            var wrote = LastVisitedMru.ProgramOf(Convert.FromBase64String(setting.Written));
            if (program is null || !string.Equals(program, wrote, StringComparison.OrdinalIgnoreCase)
                || LastVisitedMru.IdListOf(entry) is not { } idList
                || _shell.PathOf(idList) is not { } path
                || !SamePath(path, folder))
            {
                return setting;
            }
            return setting with { Written = current };
        }
        catch (Exception ex)
        {
            Problem($"Could not read {setting.Name} back to see if it is still the class folder: {ex.Message}");
            return setting;
        }
    }

    // Full paths, normalized, compared ignoring case and a trailing separator.
    private static bool SamePath(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // The lesson's folder when it is ready to be pointed at: a plain folder on the
    // Desktop, not a link. Null otherwise, to try again at the next call.
    private string? ReadyFolder(ClassLesson lesson)
    {
        var name = lesson.FolderName;
        if (string.IsNullOrWhiteSpace(_desktopDirectory)
            || !string.Equals(FileNames.Safe(name), name, StringComparison.Ordinal))
        {
            return null;
        }

        var folder = Path.Combine(_desktopDirectory, name);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        if (ClassFolderRules.IsLink(folder) || ClassFolderRules.IsLink(_desktopDirectory))
        {
            Problem($"Not pointing the save locations at {folder}: it, or the Desktop, is a link.");
            return null;
        }
        return folder;
    }

    private void Apply(ClassLesson lesson)
    {
        var folder = ReadyFolder(lesson);
        if (folder is null)
        {
            return;
        }

        // Each target is read in its own try: one that throws (denied access, say)
        // is left out, and the others still apply.
        var settings = new List<SavedSetting>();
        ReadTarget("Downloads", () => AddIfAny(settings, TextSetting(ClassSaveTargets.DownloadsKey,
            ClassSaveTargets.DownloadsName, RegistryValueKind.ExpandString, folder)));

        var hasOffice = false;
        ReadTarget(ClassSaveTargets.OfficeRoot, () =>
        {
            using var office = _software.OpenSubKey(ClassSaveTargets.OfficeRoot);
            hasOffice = office is not null;
        });
        if (hasOffice)
        {
            foreach (var (key, name, kind) in ClassSaveTargets.OfficeFolders)
            {
                ReadTarget(name, () => AddIfAny(settings, TextSetting(key, name, kind, folder)));
            }
            ReadTarget(ClassSaveTargets.PreferCloudName, () => AddIfAny(settings, PreferCloudSetting()));
        }

        List<int>? order = null;
        ReadTarget("Notepad and Paint", () => order = AddMru(settings, folder));

        // Saved first: if it can't be, nothing is written and the next call tries again.
        var state = new ClassSaveLocationsState(lesson.LessonId, folder, settings);
        try
        {
            ClassSaveLocationsFile.Save(_statePath, state);
        }
        catch (Exception ex)
        {
            Problem($"Could not remember the class save locations in {_statePath}: {ex.Message}");
            return;
        }
        _state = state;

        var written = settings.Select(setting => Write(setting, order)).ToList();
        if (!written.SequenceEqual(settings))
        {
            // Saved again, or after a restart the undo would not know what the shell stored.
            _state = state with { Settings = written };
            SaveState(_state);
        }
    }

    private void ReadTarget(string target, Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex)
        {
            Problem($"Could not read {target} to point it at the class folder: {ex.Message}");
        }
    }

    private static void AddIfAny(List<SavedSetting> settings, SavedSetting? setting)
    {
        if (setting is not null)
        {
            settings.Add(setting);
        }
    }

    // A string target. The kind written is the kind already there (so the value
    // looks untouched), or the target's own when there is none. A value of any
    // other kind is the student's business: skipped.
    private SavedSetting? TextSetting(string key, string name, RegistryValueKind defaultKind, string folder)
    {
        var before = ClassSaveTargets.ReadEncoded(_software, key, name, out var kind);
        if (before is null && kind != RegistryValueKind.Unknown)
        {
            return null;
        }

        var setting = new SavedSetting(key, name, before is null ? defaultKind : kind, before, folder);
        if (before is not null && (ClassSaveTargets.SameValue(before, kind, setting) || IsAClassFolder(before)))
        {
            // Already this class folder (the state was lost mid-lesson) or an earlier
            // one (lost in an earlier lesson, and that folder is recycled). It is not
            // what the student had, so put back Windows' default (Downloads) or nothing.
            setting = Is(setting, ClassSaveTargets.DownloadsKey)
                ? setting with { Kind = RegistryValueKind.ExpandString, Before = DefaultDownloads }
                : setting with { Before = null };
        }
        return ClassSaveTargets.IsAllowed(setting) ? setting : null;
    }

    // Whether the path's last part is a class folder's name ("Pasta da turma – <turma>").
    private static bool IsAClassFolder(string path) =>
        Path.GetFileName(path.TrimEnd('\\', '/')).StartsWith(ClassLesson.FolderPrefix, StringComparison.OrdinalIgnoreCase);

    private SavedSetting? PreferCloudSetting()
    {
        var before = ClassSaveTargets.ReadEncoded(_software, ClassSaveTargets.PreferCloudKey,
            ClassSaveTargets.PreferCloudName, out var kind);
        if (kind != RegistryValueKind.Unknown && (before is null || kind != RegistryValueKind.DWord))
        {
            return null;
        }

        var setting = new SavedSetting(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName,
            RegistryValueKind.DWord, before, "0");
        return ClassSaveTargets.IsAllowed(setting) ? setting : null;
    }

    // Adds the Notepad and Paint entries and returns MRUListEx as it is now, or
    // null when they are left alone: an order that can't be trusted, or a folder
    // the shell can't give an ID list for. An entry the program already has keeps
    // its number; a new one takes the smallest free number, which is neither the
    // name of a value there nor in the order (kept up to date as numbers are taken).
    // Nothing is added to settings unless both entries are ready.
    private List<int>? AddMru(List<SavedSetting> settings, string folder)
    {
        var encoded = ClassSaveTargets.ReadEncoded(_software, ClassSaveTargets.MruKey,
            LastVisitedMru.OrderName, out var kind);
        var order = encoded is null
            ? kind == RegistryValueKind.Unknown ? LastVisitedMru.ParseOrder(null) : null
            : kind == RegistryValueKind.Binary ? LastVisitedMru.ParseOrder(Convert.FromBase64String(encoded)) : null;
        if (order is null)
        {
            Problem($"Not pointing Notepad and Paint at the class folder: {LastVisitedMru.OrderName} is not a list that can be trusted ({kind}).");
            return null;
        }

        var idList = _shell.IdListOf(folder);
        if (idList is null)
        {
            Problem($"Not pointing Notepad and Paint at the class folder: the shell gave no ID list for {folder}.");
            return null;
        }

        var taken = new HashSet<int>(order);
        var existing = new Dictionary<string, (int Number, string Entry)>(StringComparer.OrdinalIgnoreCase);
        using (var mru = _software.OpenSubKey(ClassSaveTargets.MruKey))
        {
            foreach (var valueName in mru?.GetValueNames() ?? Array.Empty<string>())
            {
                if (!IsEntryNumber(valueName, out var number))
                {
                    continue;
                }

                taken.Add(number);
                var entry = ClassSaveTargets.ReadEncoded(_software, ClassSaveTargets.MruKey, valueName, out var entryKind);
                if (entry is not null && entryKind == RegistryValueKind.Binary
                    && LastVisitedMru.ProgramOf(Convert.FromBase64String(entry)) is { } program)
                {
                    existing.TryAdd(program, (number, entry));
                }
            }
        }

        var entries = new List<SavedSetting>();
        foreach (var program in ClassSaveTargets.MruPrograms)
        {
            string name;
            string? before = null;
            if (existing.TryGetValue(program, out var found))
            {
                name = found.Number.ToString(CultureInfo.InvariantCulture);
                before = found.Entry;
            }
            else
            {
                var number = 0;
                while (taken.Contains(number))
                {
                    number++;
                }
                taken.Add(number);
                name = number.ToString(CultureInfo.InvariantCulture);
            }

            var written = Convert.ToBase64String(LastVisitedMru.Entry(program, idList));
            // The entry Midview wrote already (state lost mid-lesson) is not the student's.
            if (before == written)
            {
                before = null;
            }
            var setting = new SavedSetting(ClassSaveTargets.MruKey, name, RegistryValueKind.Binary, before, written);
            AddIfAny(entries, ClassSaveTargets.IsAllowed(setting) ? setting : null);
        }
        settings.AddRange(entries);
        return order;
    }

    private static bool IsEntryNumber(string name, out int number)
    {
        number = 0;
        return name.Length is >= 1 and <= 4 && name.All(c => c is >= '0' and <= '9')
            && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool Is(SavedSetting setting, string key, string? name = null) =>
        string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase)
        && (name is null || string.Equals(setting.Name, name, StringComparison.OrdinalIgnoreCase));

    // One setting, in its own try/catch: a refusal costs only this one. Returns
    // the setting as it is to be remembered (see StoredDownloads).
    private SavedSetting Write(SavedSetting setting, List<int>? order)
    {
        try
        {
            if (Is(setting, ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName))
            {
                _shell.SetDownloads(setting.Written);
                return StoredDownloads(setting);
            }
            else
            {
                ClassSaveTargets.WriteEncoded(_software, setting.Key, setting.Name, setting.Kind, setting.Written);
            }

            if (order is not null && Is(setting, ClassSaveTargets.MruKey)
                && int.TryParse(setting.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                // Each program's entry goes to the front, as the dialog itself does.
                order.Remove(number);
                order.Insert(0, number);
                ClassSaveTargets.WriteEncoded(_software, ClassSaveTargets.MruKey, LastVisitedMru.OrderName,
                    RegistryValueKind.Binary, Convert.ToBase64String(LastVisitedMru.FormatOrder(order)));
            }
        }
        catch (Exception ex)
        {
            Problem($"Could not point {setting.Name} at the class folder: {ex.Message}");
        }
        return setting;
    }

    // The shell might store Downloads in another form than it was given (this
    // was checked on Windows 11, not on Windows 10). What it stored is what the
    // undo must recognize as Midview's, so that is what is remembered.
    private SavedSetting StoredDownloads(SavedSetting setting)
    {
        try
        {
            var stored = ClassSaveTargets.ReadEncoded(_software, setting.Key, setting.Name, out var kind);
            return stored is not null && kind is RegistryValueKind.String or RegistryValueKind.ExpandString
                && !ClassSaveTargets.SameValue(stored, kind, setting)
                ? setting with { Written = stored }
                : setting;
        }
        catch (Exception ex)
        {
            Problem($"Could not read Downloads back after pointing it at the class folder: {ex.Message}");
            return setting;
        }
    }

    // Said once, not every few seconds for as long as it lasts.
    private void Problem(string text)
    {
        if (_problems.Add(text))
        {
            _log(text);
        }
    }
}
