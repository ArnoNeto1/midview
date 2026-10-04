using Midview.Protocol;
using Midview.Session;
using Xunit;

public class OpenProgramsListTests
{
    private const int Own = 999;

    private static TopLevelWindow Window(
        int processId, string processName, string title = "Janela", string className = "Chrome_WidgetWin_1",
        bool visible = true, bool cloaked = false, bool owned = false, bool tool = false) =>
        new(IntPtr.Zero, processId, processName, className, title, visible, cloaked, owned, tool);

    private static (string ExeName, string DisplayName)? Describe(int processId) => processId switch
    {
        1 => ("chrome.exe", "Google Chrome"),
        2 => ("chrome.exe", "Google Chrome"),
        3 => ("WINWORD.EXE", "Microsoft Word"),
        4 => ("explorer.exe", "Windows Explorer"),
        5 => ("MidviewChat.exe", "Chat"),
        6 => ("bad*name", "x"),
        7 => ("soffice.bin", "LibreOffice"),
        _ => null,
    };

    [Fact]
    public void ListsTheStudentsProgramsOnce_WithTheirWindowCount()
    {
        var list = OpenProgramsList.Build(new[]
        {
            Window(1, "chrome"), Window(1, "chrome", title: "Outra aba"), Window(2, "chrome"),
            Window(3, "winword", title: "Relatório"),
        }, Own, Describe);

        Assert.Equal(new[] { "chrome.exe", "winword.exe" }, list.Select(program => program.ExeName));
        // Two processes of chrome each with a window: counted per process.
        Assert.Equal(2, list.Single(program => program.ExeName == "chrome.exe").WindowCount);
        Assert.Equal("Microsoft Word", list.Single(program => program.ExeName == "winword.exe").DisplayName);
    }

    [Fact]
    public void AProgramWhoseImageIsNotAnExe_IsOfferedByItsNameWithExe()
    {
        var list = OpenProgramsList.Build(new[] { Window(7, "soffice") }, Own, Describe);

        Assert.Equal("soffice.exe", Assert.Single(list).ExeName);
    }

    [Fact]
    public void NeverOffersTheShellOrMidview()
    {
        var list = OpenProgramsList.Build(new[]
        {
            Window(4, "explorer", className: "CabinetWClass"), Window(5, "MidviewChat"), Window(Own, "MidviewSession"),
        }, Own, Describe);

        Assert.Empty(list);
    }

    [Fact]
    public void LeavesOutWindowsAltTabDoesntShow()
    {
        var list = OpenProgramsList.Build(new[]
        {
            Window(1, "chrome", visible: false), Window(1, "chrome", cloaked: true), Window(1, "chrome", owned: true),
            Window(1, "chrome", tool: true), Window(1, "chrome", title: "  "),
        }, Own, Describe);

        Assert.Empty(list);
    }

    [Fact]
    public void LeavesOutAProcessItCantDescribe_OrWithAnInvalidName() =>
        Assert.Empty(OpenProgramsList.Build(new[] { Window(6, "x"), Window(77, "y") }, Own, Describe));
}
