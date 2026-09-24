using System.Buffers.Binary;
using Midview.Protocol;
using Xunit;

public class FileTransferTests
{
    [Fact]
    public void Header_RoundTripsNameAndSize()
    {
        var header = FileTransfer.EncodeHeader("Exercícios – capítulo 3.pdf", 123_456_789);

        Assert.True(FileTransfer.TryDecodeHeader(header, out var name, out var size));
        Assert.Equal("Exercícios – capítulo 3.pdf", name);
        Assert.Equal(123_456_789, size);
    }

    [Fact]
    public void Header_EmptyFileIsAllowed()
    {
        Assert.True(FileTransfer.TryDecodeHeader(FileTransfer.EncodeHeader("vazio.txt", 0), out _, out var size));
        Assert.Equal(0, size);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(FileTransfer.MaxFileSize + 1)]
    public void Header_SizeOutOfRange_IsRejected(long size)
    {
        Assert.False(FileTransfer.TryDecodeHeader(FileTransfer.EncodeHeader("a.txt", size), out _, out _));
    }

    [Fact]
    public void Header_WithoutAName_IsRejected()
    {
        Assert.False(FileTransfer.TryDecodeHeader(FileTransfer.EncodeHeader("   ", 10), out _, out _));
        Assert.False(FileTransfer.TryDecodeHeader(new byte[8], out _, out _));
        Assert.False(FileTransfer.TryDecodeHeader(new byte[3], out _, out _));
    }

    [Fact]
    public void Header_InvalidUtf8Name_IsRejected()
    {
        var header = new byte[8 + 3];
        BinaryPrimitives.WriteInt64BigEndian(header, 10);
        new byte[] { 0x4F, 0xC3, 0x28 }.CopyTo(header, 8);

        Assert.False(FileTransfer.TryDecodeHeader(header, out _, out _));
    }
}
