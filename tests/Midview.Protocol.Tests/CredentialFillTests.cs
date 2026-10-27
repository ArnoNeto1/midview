using System.Text;
using Midview.Protocol;
using Xunit;

namespace Midview.Protocol.Tests;

public class CredentialFillTests
{
    [Fact]
    public void Encode_then_TryDecode_round_trips()
    {
        byte[] password = Encoding.UTF8.GetBytes("Senhã#2024!");

        byte[] payload = CredentialFill.Encode(password);

        Assert.True(CredentialFill.TryDecode(payload, out byte[] decoded));
        Assert.Equal(password, decoded);
        Assert.NotSame(payload, decoded);
    }

    [Fact]
    public void TryDecode_rejects_empty_and_oversize()
    {
        Assert.False(CredentialFill.TryDecode(new byte[0], out byte[] empty));
        Assert.Empty(empty);

        Assert.False(CredentialFill.TryDecode(new byte[CredentialFill.MaxBytes + 1], out byte[] oversize));
        Assert.Empty(oversize);

        Assert.True(CredentialFill.TryDecode(new byte[CredentialFill.MaxBytes], out byte[] max));
        Assert.Equal(CredentialFill.MaxBytes, max.Length);
    }

    [Fact]
    public void Encode_rejects_empty_and_oversize()
    {
        Assert.Throws<ArgumentException>(() => CredentialFill.Encode(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => CredentialFill.Encode(new byte[CredentialFill.MaxBytes + 1]));
    }
}
