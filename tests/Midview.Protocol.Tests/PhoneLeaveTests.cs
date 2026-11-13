using Midview.Protocol;
using Xunit;

// "Desfazer e tirar o Midview": message 106, confirmed by the word the
// teacher typed.
public class PhoneLeaveTests
{
    [Fact]
    public void MessageType_IsTheWireNumber() => Assert.Equal(106, (int)MessageType.RemoveMidviewFromPhone);

    [Fact]
    public void TheCommand_RoundTripsThroughTheFramer()
    {
        using var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.RemoveMidviewFromPhone, PhoneLeave.Encode()));
        stream.Position = 0;

        var read = MessageFramer.Read(stream);

        Assert.Equal(MessageType.RemoveMidviewFromPhone, read.Type);
        Assert.Equal("REMOVER"u8.ToArray(), read.Payload);
        Assert.True(PhoneLeave.IsConfirmed(read.Payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("remover")]
    [InlineData("REMOVE")]
    [InlineData("REMOVER ")]
    [InlineData("APAGAR")]
    public void AnyOtherPayload_IsNotAConfirmation(string payload) =>
        Assert.False(PhoneLeave.IsConfirmed(System.Text.Encoding.UTF8.GetBytes(payload)));

    [Fact]
    public void TheWords_AreUpperCaseAndDifferent()
    {
        Assert.Equal("REMOVER", PhoneLeave.Word);
        Assert.Equal("APAGAR", PhoneLeave.WipeWord);
    }
}
