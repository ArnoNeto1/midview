using Midview.Session;
using Xunit;

public class AppClosePolicyTests
{
    private const int OwnProcessId = 4242;

    private static TopLevelWindow Window(
        string title = "Documento1 - Word", string processName = "WINWORD", string className = "OpusApp",
        int processId = 100, bool visible = true, bool cloaked = false, bool owned = false, bool toolWindow = false) =>
        new(new IntPtr(processId * 10), processId, processName, className, title, visible, cloaked, owned, toolWindow);

    [Fact]
    public void AnOrdinaryProgramWindow_IsClosed()
    {
        var word = Window();

        Assert.Equal(new[] { word }, AppClosePolicy.WindowsToClose(new[] { word }, OwnProcessId));
    }

    [Theory]
    [InlineData("Progman")] // the desktop
    [InlineData("WorkerW")] // the desktop, behind the icons
    [InlineData("Shell_TrayWnd")] // the taskbar
    [InlineData("Shell_SecondaryTrayWnd")] // the taskbar on a second screen
    public void TheShell_IsNeverClosed(string className)
    {
        var shell = Window(title: "Program Manager", processName: "explorer", className: className);

        Assert.Empty(AppClosePolicy.WindowsToClose(new[] { shell }, OwnProcessId));
    }

    [Theory]
    [InlineData("Iniciar", "StartMenuExperienceHost")]
    [InlineData("Pesquisar", "SearchHost")]
    [InlineData("Central de Notificações", "ShellExperienceHost")]
    public void TheShellsPanels_AreNeverClosed_OrForced(string title, string processName)
    {
        var panel = Window(title: title, processName: processName, className: "Windows.UI.Core.CoreWindow");

        Assert.Empty(AppClosePolicy.WindowsToClose(new[] { panel }, OwnProcessId));
        Assert.False(AppClosePolicy.MayForceClose(processName));
    }

    [Fact]
    public void StoreApps_AreClosedThroughTheirFrame()
    {
        var calculator = Window(title: "Calculadora", processName: "ApplicationFrameHost", className: "ApplicationFrameWindow");

        Assert.Single(AppClosePolicy.WindowsToClose(new[] { calculator }, OwnProcessId));
    }

    [Fact]
    public void MidviewsOwnWindows_AreNeverClosed()
    {
        var ownWindow = Window(title: "Mensagem do professor", processName: "MidviewSession", processId: OwnProcessId);
        var otherMidview = Window(title: "Midview - Sala de aula", processName: "MidviewMaster", processId: 200);

        Assert.Empty(AppClosePolicy.WindowsToClose(new[] { ownWindow, otherMidview }, OwnProcessId));
    }

    [Fact]
    public void WindowsAltTabDoesNotShow_AreLeftAlone()
    {
        var windows = new[]
        {
            Window(visible: false),
            Window(cloaked: true), // another virtual desktop, or a suspended Store app
            Window(owned: true), // a dialog: it closes with its owner
            Window(toolWindow: true),
            Window(title: "   "),
        };

        Assert.Empty(AppClosePolicy.WindowsToClose(windows, OwnProcessId));
    }

    [Fact]
    public void FileExplorerWindows_AreAskedToClose_ButExplorerIsNeverForced()
    {
        var folder = Window(title: "Documentos", processName: "explorer", className: "CabinetWClass");

        Assert.Single(AppClosePolicy.WindowsToClose(new[] { folder }, OwnProcessId));
        Assert.False(AppClosePolicy.MayForceClose("explorer"));
        Assert.False(AppClosePolicy.MayForceClose("EXPLORER"));
    }

    [Theory]
    [InlineData("ApplicationFrameHost")]
    [InlineData("MidviewSession")]
    [InlineData("MidviewAgent")]
    public void ProgramsHostingMoreThanOneApp_OrMidview_AreNeverForced(string processName)
    {
        Assert.False(AppClosePolicy.MayForceClose(processName));
    }

    [Theory]
    [InlineData("WINWORD")]
    [InlineData("chrome")]
    [InlineData("notepad")]
    public void OrdinaryPrograms_MayBeForced(string processName)
    {
        Assert.True(AppClosePolicy.MayForceClose(processName));
    }
}
