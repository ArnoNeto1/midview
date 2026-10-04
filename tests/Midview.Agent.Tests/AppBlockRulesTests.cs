using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class AppBlockRulesTests
{
    // By default an installed program: in Program Files, owned by an
    // administrator. inWindows implies a trusted folder; inTrusted alone is
    // Program Files; a student's file is Student, a file on a pendrive or
    // whose owner can't be read is Other.
    private static ProcessFacts Process(
        string imageName, string? originalFilename = null, int sessionId = 1, bool ownedBySessionUser = true,
        bool elevated = false, bool inWindows = false, bool inTrusted = true, FileOwnerKind owner = FileOwnerKind.Trusted) =>
        new(100, DateTime.UnixEpoch, sessionId, ownedBySessionUser, elevated, imageName, originalFilename, inWindows,
            inWindows || inTrusted, owner, imageName);

    private static AppBlockPolicy BlockList(params string[] programs) => new(1, AppBlockMode.BlockList, programs);

    private static AppBlockPolicy AllowOnly(params string[] programs) => new(1, AppBlockMode.AllowOnly, programs);

    [Fact]
    public void None_EndsNothing() =>
        Assert.False(AppBlockRules.ShouldEnd(AppBlockPolicy.Lifted(1), Process("chrome.exe")));

    [Fact]
    public void BlockList_EndsAListedProgram() =>
        Assert.True(AppBlockRules.ShouldEnd(BlockList("chrome.exe"), Process("chrome.exe")));

    [Fact]
    public void BlockList_LeavesOthersAlone() =>
        Assert.False(AppBlockRules.ShouldEnd(BlockList("chrome.exe"), Process("winword.exe")));

    [Fact]
    public void BlockList_CatchesARenamedCopy_ByOriginalFilename() =>
        Assert.True(AppBlockRules.ShouldEnd(BlockList("chrome.exe"),
            Process("jogo.exe", originalFilename: "chrome.exe", owner: FileOwnerKind.Student)));

    [Fact]
    public void AllowOnly_EndsAnUnlistedProgram() =>
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"), Process("chrome.exe")));

    [Fact]
    public void AllowOnly_KeepsAListedProgram() =>
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"), Process("winword.exe")));

    [Fact]
    public void AllowOnly_KeepsWindowsItself() =>
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("sometool.exe", inWindows: true)));

    [Fact]
    public void AllowOnly_KeepsAnInstalledWindowsHelper_ButNotAFileThatOnlyCarriesItsName()
    {
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("msedgewebview2.exe", inTrusted: true)));
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("msedgewebview2.exe", inTrusted: false)));
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("msedgewebview2.exe", owner: FileOwnerKind.Other)));
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("wscript.exe")]
    [InlineData("mshta.exe")]
    [InlineData("wsl.exe")]
    public void AllowOnly_EndsAScriptHostInTheWindowsFolder_UnlessItIsListed(string host)
    {
        // They run whatever the student types: part of Windows, but not "Windows itself" here.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process(host, inWindows: true)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(host), Process(host, inWindows: true)));
    }

    [Fact]
    public void BlockList_LeavesAScriptHostAloneUnlessListed()
    {
        Assert.False(AppBlockRules.ShouldEnd(BlockList("chrome.exe"), Process("cmd.exe", inWindows: true)));
        Assert.True(AppBlockRules.ShouldEnd(BlockList("cmd.exe"), Process("cmd.exe", inWindows: true)));
    }

    [Theory]
    [InlineData(FileOwnerKind.Student)]
    [InlineData(FileOwnerKind.Other)]
    public void AllowOnly_AFileInTheWindowsFolderThatNoTrustedOwnerInstalled_IsNotWindows(FileOwnerKind owner)
    {
        // C:\Windows\Temp and similar folders are writable by students.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("x.exe", inWindows: true, owner: owner)));
    }

    [Fact]
    public void AllowOnly_EndsAStudentFileInsideTheWindowsFolder()
    {
        // A student-writable folder under C:\Windows: the file is theirs.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(),
            Process("x.exe", inWindows: true, owner: FileOwnerKind.Student)));
    }

    [Fact]
    public void AllowOnly_AnInstalledProgramIsAllowedByName_EvenWithoutVersionInformation() =>
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"), Process("winword.exe", originalFilename: null)));

    [Theory]
    [InlineData(FileOwnerKind.Student)]
    [InlineData(FileOwnerKind.Other)]
    public void AllowOnly_AFileNoTrustedOwnerInstalled_MustMatchItsOriginalFilename(FileOwnerKind owner)
    {
        // A file named winword.exe but without Word's identity -- the student's
        // own, one on a pendrive, one whose owner can't be read -- is ended.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"),
            Process("winword.exe", originalFilename: "cheat.exe", inTrusted: false, owner: owner)));
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"),
            Process("winword.exe", originalFilename: null, inTrusted: false, owner: owner)));
        // The real thing (its OriginalFilename matches) is kept.
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"),
            Process("winword.exe", originalFilename: "winword.exe", inTrusted: false, owner: owner)));
    }

    [Fact]
    public void NeverEndsTheShell_EvenIfListed()
    {
        Assert.False(AppBlockRules.ShouldEnd(BlockList(), Process("explorer.exe", inWindows: true)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("dwm.exe", inWindows: true)));
    }

    [Fact]
    public void NeverEndsMidview() =>
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("midviewsession.exe", inTrusted: true)));

    [Fact]
    public void NeverEndsAnotherSessionOrAnotherUserOrElevated()
    {
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("chrome.exe", sessionId: 0)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("chrome.exe", ownedBySessionUser: false)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("chrome.exe", elevated: true)));
    }

    [Theory]
    [InlineData(FileOwnerKind.Student)]
    [InlineData(FileOwnerKind.Other)]
    public void AProtectedNameOnAFileNoTrustedOwnerInstalled_LosesItsProtection(FileOwnerKind owner)
    {
        // An exe renamed to explorer.exe -- the student's, one on a pendrive
        // (no owner on FAT), or one whose owner can't be read -- is not the real shell.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("explorer.exe", owner: owner)));
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("midviewsession.exe", owner: owner)));
    }

    [Fact]
    public void AProtectedNameOutsideTheTrustedFolders_LosesItsProtection()
    {
        // Even with an administrator as the owner: it isn't where the real one lives.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("explorer.exe", inTrusted: false)));
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(), Process("midviewsession.exe", inTrusted: false)));
    }

    [Fact]
    public void MatchesBlockList_IsAboutTheListedName_NotTheFence()
    {
        Assert.True(AppBlockRules.MatchesBlockList(BlockList("chrome.exe"), Process("chrome.exe", elevated: true)));
        Assert.True(AppBlockRules.MatchesBlockList(BlockList("chrome.exe"), Process("jogo.exe", originalFilename: "chrome.exe")));
        Assert.False(AppBlockRules.MatchesBlockList(BlockList("chrome.exe"), Process("winword.exe")));
        Assert.False(AppBlockRules.MatchesBlockList(AllowOnly("chrome.exe"), Process("chrome.exe")));
    }

    [Fact]
    public void LeftAloneBecause_SaysWhichPartOfTheFenceKeptAProgramOut()
    {
        Assert.Null(AppBlockRules.LeftAloneBecause(Process("chrome.exe")));
        Assert.Contains("interactive session", AppBlockRules.LeftAloneBecause(Process("chrome.exe", sessionId: 0)));
        Assert.Contains("logged on", AppBlockRules.LeftAloneBecause(Process("chrome.exe", ownedBySessionUser: false)));
        Assert.Contains("elevated", AppBlockRules.LeftAloneBecause(Process("chrome.exe", elevated: true)));
        Assert.Contains("protected", AppBlockRules.LeftAloneBecause(Process("explorer.exe", inWindows: true)));
    }
}
