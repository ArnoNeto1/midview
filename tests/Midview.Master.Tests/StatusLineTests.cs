using Midview.Master;
using Xunit;

// What a window opening from the main window does to the status bar's line.
public class StatusLineTests
{
    [Fact]
    public void AnEarlierActionsResult_Goes()
    {
        Assert.True(StatusLine.ClearsOnWindow("Telas desbloqueadas.", noticeJustWritten: false, warning: null));
    }

    [Fact]
    public void ANoticeTheSameClickWrote_Stays()
    {
        Assert.False(StatusLine.ClearsOnWindow("1 celular ficou de fora: ...", noticeJustWritten: true, warning: null));
    }

    [Fact]
    public void AWarning_StaysWhileItIsTheLine()
    {
        const string warning = "O bloqueio foi enviado, mas não pôde ser guardado neste PC: disco cheio";

        Assert.False(StatusLine.ClearsOnWindow(warning, noticeJustWritten: false, warning));
        // Once another line took its place, that line is a result like any other.
        Assert.True(StatusLine.ClearsOnWindow("Telas desbloqueadas.", noticeJustWritten: false, warning));
    }

    // An empty bar is filled again: "Telas bloqueadas" while screens are locked.
    [Fact]
    public void AnEmptyBar_IsFilledAgain()
    {
        Assert.True(StatusLine.ClearsOnWindow(string.Empty, noticeJustWritten: false, warning: null));
    }
}
