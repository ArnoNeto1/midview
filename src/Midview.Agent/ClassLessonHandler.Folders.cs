using System.Security;
using System.Security.Principal;
using Midview.Protocol;

namespace Midview.Agent;

// ClassLessonHandler: the logged-on student's Desktop and download folder
// made to match the lesson, with the lesson's lock held.
public sealed partial class ClassLessonHandler
{
    // The class folder Protect last succeeded on (student, Desktop, name), so
    // later polls only check it; any other means Protect runs again. A folder
    // given back keeps its owner, so its owner alone can't say it is protected.
    private string? _protectedFor;

    // The class folders already given back (student and path), so a poll
    // doesn't give them back again.
    private readonly HashSet<string> _released = new(StringComparer.OrdinalIgnoreCase);

    // A policy write that failed may have left Chrome's value right and
    // Edge's not: the next look writes it again even if Chrome's matches.
    private bool _policyUnsure;

    // The logged-on student's Desktop and download folder made to match the
    // lesson. True when the folder Chrome saves into changed. desktop: null
    // when there is none the Agent may work on (UserSessionProcessLauncher.
    // LoggedOnStudent): then only the download folder Midview set is removed,
    // with no lesson on, as that needs no Desktop.
    private bool FollowLesson(
        ClassLesson lesson, string? desktop, SecurityIdentifier student, List<string> done, List<string> problems)
    {
        // Opened as itself: a Desktop that is missing or a link (redirected
        // elsewhere) gets nothing, not even the download folder. With no
        // lesson on there was nothing to put there either.
        var desktopUsable = desktop is not null && ProtectedStudentFolders.IsRealDirectory(desktop);
        if (!desktopUsable)
        {
            if (lesson.IsOn)
            {
                problems.Add(desktop is null
                    ? "Área de Trabalho do aluno não encontrada, ou fora da pasta do aluno e não é dele"
                    : $"Área de Trabalho não encontrada ou não é uma pasta comum ({desktop})");
            }
        }
        else
        {
            if (lesson.IsOn)
            {
                ProtectClassFolder(lesson, desktop!, student, done, problems);
            }
            else
            {
                _protectedFor = null;
            }
            ReleaseOtherClassFolders(lesson, desktop!, student, done, problems);
        }

        if (!lesson.IsOn)
        {
            return PointDownloads(student, null, done, problems);
        }
        return desktopUsable && PointDownloads(student, Path.Combine(desktop!, lesson.FolderName), done, problems);
    }

    private void ProtectClassFolder(
        ClassLesson lesson, string desktop, SecurityIdentifier student, List<string> done, List<string> problems)
    {
        var names = new[] { lesson.FolderName };
        var folder = Path.Combine(desktop, lesson.FolderName);
        var key = $"{student.Value}|{folder}";
        _released.Remove(key);
        if (string.Equals(_protectedFor, key, StringComparison.OrdinalIgnoreCase)
            && !ProtectedStudentFolders.NeedsProtecting(desktop, _folderOwner, names))
        {
            return;
        }

        var folderProblems = ProtectedStudentFolders.Protect(desktop, student, _folderOwner, names);
        if (folderProblems.Count == 0)
        {
            _protectedFor = key;
            done.Add($"protected {folder}");
        }
        else
        {
            _protectedFor = null;
            problems.AddRange(folderProblems);
        }
    }

    // Every class folder on the Desktop but the lesson's own is given back
    // (once each). A link of that name is left alone.
    private void ReleaseOtherClassFolders(
        ClassLesson lesson, string desktop, SecurityIdentifier student, List<string> done, List<string> problems)
    {
        List<string> names;
        try
        {
            names = new DirectoryInfo(desktop).EnumerateDirectories()
                .Where(folder => folder.Name.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)
                    && !(lesson.IsOn && string.Equals(folder.Name, lesson.FolderName, StringComparison.OrdinalIgnoreCase))
                    && (folder.Attributes & FileAttributes.ReparsePoint) == 0
                    && !_released.Contains($"{student.Value}|{Path.Combine(desktop, folder.Name)}"))
                .Select(folder => folder.Name)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            problems.Add($"Área de Trabalho ({desktop}): {ErrorText.Describe(ex)}");
            return;
        }

        foreach (var name in names)
        {
            var folderProblems = ProtectedStudentFolders.Release(desktop, student, new[] { name });
            if (folderProblems.Count == 0)
            {
                _released.Add($"{student.Value}|{Path.Combine(desktop, name)}");
                done.Add($"gave {name} back to the student");
            }
            else
            {
                problems.AddRange(folderProblems);
            }
        }
    }

    // Chrome's and Edge's download folder set to folder (null: removed, where
    // it is a class folder). True when Chrome's changed.
    private bool PointDownloads(SecurityIdentifier student, string? folder, List<string> done, List<string> problems)
    {
        try
        {
            using var software = _openUserSoftware(student);
            if (software is null)
            {
                problems.Add("o registro do aluno não está carregado; a pasta de downloads do navegador fica para depois");
                return false;
            }

            var before = DownloadFolderPolicy.Current(software);
            if (before == folder && !_policyUnsure)
            {
                return false;
            }

            Exception? failure = null;
            try
            {
                if (folder is null)
                {
                    DownloadFolderPolicy.Remove(software);
                }
                else
                {
                    DownloadFolderPolicy.Apply(software, folder);
                }
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                failure = ex;
            }
            _policyUnsure = failure is not null;

            var after = DownloadFolderPolicy.Current(software);
            if (failure is not null)
            {
                problems.Add($"pasta de downloads do navegador: {ErrorText.Describe(failure)}");
            }
            if (after == before)
            {
                return false;
            }
            done.Add(after is null ? "removed the browsers' download folder" : $"pointed the browsers' downloads at {after}");
            return true;
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            _policyUnsure = true;
            problems.Add($"pasta de downloads do navegador: {ErrorText.Describe(ex)}");
            return false;
        }
    }

    private static bool IsRegistryError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;
}
