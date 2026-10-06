using Midview.Protocol;
using Xunit;

public class ErrorTextTests
{
    // .NET's own messages come in English whatever the Windows language;
    // these are the ones a teacher actually runs into.
    [Fact]
    public void AccessDenied_IsInPortuguese() =>
        Assert.Equal("acesso negado",
            ErrorText.Describe(new UnauthorizedAccessException(@"Access to the path 'C:\Windows\system32\x' is denied.")));

    [Fact]
    public void AMissingFolder_IsInPortuguese() =>
        Assert.Equal("pasta não encontrada", ErrorText.Describe(new DirectoryNotFoundException("Could not find a part of the path.")));

    [Fact]
    public void AMissingFile_IsInPortuguese() =>
        Assert.Equal("arquivo não encontrado", ErrorText.Describe(new FileNotFoundException("Could not find file.")));

    [Theory]
    [InlineData(unchecked((int)0x80070070))] // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027))] // ERROR_HANDLE_DISK_FULL
    public void AFullDisk_IsInPortuguese(int hresult) =>
        Assert.Equal("disco cheio", ErrorText.Describe(new IOException("There is not enough space on the disk.", hresult)));

    [Theory]
    [InlineData(unchecked((int)0x80070020))] // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021))] // ERROR_LOCK_VIOLATION
    public void AFileInUse_IsInPortuguese(int hresult) =>
        Assert.Equal("o arquivo está em uso por outro programa",
            ErrorText.Describe(new IOException("The process cannot access the file.", hresult)));

    // Anything else keeps its own text: better something technical than nothing.
    [Fact]
    public void AnythingElse_KeepsItsOwnText() =>
        Assert.Equal("algo inesperado", ErrorText.Describe(new InvalidOperationException("algo inesperado")));
}
