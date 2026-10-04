using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class AppBlockRulesTests
{
    private static ProcessFacts Process(
        string imageName, string? originalFilename = null, int sessionId = 1, bool ownedBySessionUser = true,
        bool elevated = false, bool inWindows = false, FileOwnerKind owner = FileOwnerKind.Installed) =>
        new(100, DateTime.UnixEpoch, sessionId, ownedBySessionUser, elevated, imageName, originalFilename, inWindows, owner, imageName);

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
    public void AllowOnly_EndsAStudentFileInsideTheWindowsFolder()
    {
        // A student-writable folder under C:\Windows: the file is theirs.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(),
            Process("x.exe", inWindows: true, owner: FileOwnerKind.Student)));
    }

    [Fact]
    public void AllowOnly_AStudentCopyOfAnAllowedName_MustMatchItsOriginalFilename()
    {
        // A student file named winword.exe but without Word's identity is ended.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"),
            Process("winword.exe", originalFilename: "cheat.exe", owner: FileOwnerKind.Student)));
        // The real thing (its OriginalFilename matches) is kept.
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly("winword.exe"),
            Process("winword.exe", originalFilename: "winword.exe", owner: FileOwnerKind.Student)));
    }

    [Fact]
    public void NeverEndsTheShell_EvenIfListed()
    {
        Assert.False(AppBlockRules.ShouldEnd(BlockList(), Process("explorer.exe", inWindows: true)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("dwm.exe", inWindows: true)));
    }

    [Fact]
    public void NeverEndsMidview() =>
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("midviewsession.exe")));

    [Fact]
    public void NeverEndsAnotherSessionOrAnotherUserOrElevated()
    {
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("chrome.exe", sessionId: 0)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("chrome.exe", ownedBySessionUser: false)));
        Assert.False(AppBlockRules.ShouldEnd(AllowOnly(), Process("chrome.exe", elevated: true)));
    }

    [Fact]
    public void AProtectedNameOnAStudentFile_LosesItsProtection()
    {
        // A student's own exe renamed to explorer.exe is not the real shell.
        Assert.True(AppBlockRules.ShouldEnd(AllowOnly(),
            Process("explorer.exe", owner: FileOwnerKind.Student)));
    }
}
