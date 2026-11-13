using System.Text.RegularExpressions;
using Midview.Master;
using Xunit;

// Google's adb (the phone cable tool) is not in the teacher's setup any
// more: the Master downloads it from Google at first use (AdbTool), and the
// notices say so. The uninstaller still stops an adb an older setup left
// running from the program's adb folder before the files go, as the setup
// does before it replaces them.
public sealed class TeacherSetupAdbTests
{
    [Fact]
    public void The_notices_say_the_platform_tools_come_from_google_at_first_use()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.txt"));

        Assert.Contains("Android SDK Platform-Tools", notices);
        Assert.Contains("adb.exe, AdbWinApi.dll e AdbWinUsbApi.dll", notices);
        Assert.Contains("Baixado do Google na primeira vez que é usado (não vem com o Midview)", notices);
        Assert.Contains(AdbToolPin.Google.Url.ToString(), notices);
        Assert.Contains(AdbTool.TermsUrl, notices);
    }

    [Fact]
    public void The_teacher_program_ships_no_adb()
    {
        var master = Path.Combine(RepoRoot(), "src", "Midview.Master");

        Assert.False(Directory.Exists(Path.Combine(master, "adb")));
        Assert.DoesNotContain("adb\\", File.ReadAllText(Path.Combine(master, "Midview.Master.csproj")));
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
