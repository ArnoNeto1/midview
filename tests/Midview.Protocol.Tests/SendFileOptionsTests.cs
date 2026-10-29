using Midview.Protocol;
using Xunit;

public class SendFileOptionsTests
{
    [Theory]
    [InlineData(true, AfterSend.OpenFile)]
    [InlineData(false, AfterSend.OpenFolder)]
    [InlineData(true, AfterSend.Nothing)]
    public void EncodeThenDecode_RoundTrips(bool overwrite, AfterSend afterSend)
    {
        var payload = FileTransfer.EncodeOptionsHeader(new SendFileOptions(overwrite, afterSend), "notas.pdf", 1234);

        Assert.True(FileTransfer.TryDecodeOptionsHeader(payload, out var options, out var name, out var size));
        Assert.Equal(new SendFileOptions(overwrite, afterSend), options);
        Assert.Equal(("notas.pdf", 1234L), (name, size));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0 })] // options but no header
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0x61 })] // no such overwrite value
    [InlineData(new byte[] { 0, 9, 0, 0, 0, 0, 0, 0, 0, 1, 0x61 })] // no such action
    public void TryDecode_RejectsAnythingElse(byte[] payload) =>
        Assert.False(FileTransfer.TryDecodeOptionsHeader(payload, out _, out _, out _));

    [Theory]
    [InlineData("trabalho.docx", true)]
    [InlineData("foto.PNG", true)]
    [InlineData("aula.pdf", true)]
    [InlineData("instalador.exe", true)]
    [InlineData("script.BAT", true)]
    [InlineData("setup.msi", true)]
    [InlineData("sem-extensao", true)]
    [InlineData("", false)]
    public void AnyNamedFile_MayBeOpenedOrExecuted(string fileName, bool mayOpen) =>
        Assert.Equal(mayOpen, FileOpenPolicy.MayOpen(fileName));
}
