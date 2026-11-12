using System.Text.RegularExpressions;
using Xunit;

// The teacher's setup carries the adb folder (the phone cable tool): the
// notices name it, and the uninstaller stops an adb left running from that
// folder before the files go, as the setup does before it replaces them.
public sealed class TeacherSetupAdbTests
{
    [Fact]
    public void The_notices_name_the_platform_tools_and_their_license()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.txt"));

        Assert.Contains("Android SDK Platform-Tools", notices);
        Assert.Contains("adb.exe, AdbWinApi.dll e AdbWinUsbApi.dll", notices);
        Assert.Contains("Apache License 2.0", notices);
        Assert.Contains("NOTICE.txt", notices);
        // The NOTICE.txt the notices point to ships in the adb folder.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "src", "Midview.Master", "adb", "NOTICE.txt")));
    }

    [Fact]
    public void The_uninstaller_stops_the_teachers_adb_before_the_files_go()
    {
        var iss = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "Midview.iss"));
        var uninstall = Regex.Match(
            iss, @"procedure CurUninstallStepChanged\(.*?\nend;", RegexOptions.Singleline).Value;
        var teacher = Regex.Match(
            uninstall, @"if TipoInstalado = TipoProfessor then begin(.*?)Exit;", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains("if CurUninstallStep <> usUninstall then", uninstall);
        Assert.Contains("PararAdbDoProfessor;", teacher);
        // Still only the program's own adb folder.
        Assert.Contains("Pasta := ExpandConstant('{app}\\adb\\');", iss);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Midview.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
