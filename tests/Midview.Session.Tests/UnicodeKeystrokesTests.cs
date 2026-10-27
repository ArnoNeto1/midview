using Midview.Session;
using Xunit;

public class UnicodeKeystrokesTests
{
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkReturn = 0x0D;

    [Fact]
    public void Builds_a_unicode_down_up_pair_per_code_unit()
    {
        var keystrokes = UnicodeKeystrokes.Build("aç", pressEnter: false);

        Assert.Equal(4, keystrokes.Count);
        for (var i = 0; i < 2; i++)
        {
            var ch = "aç"[i];
            var down = keystrokes[i * 2];
            var up = keystrokes[i * 2 + 1];

            Assert.Equal(0, down.VirtualKey);
            Assert.Equal((ushort)ch, down.ScanCode);
            Assert.Equal(KeyEventUnicode, down.Flags);

            Assert.Equal(0, up.VirtualKey);
            Assert.Equal((ushort)ch, up.ScanCode);
            Assert.Equal(KeyEventUnicode | KeyEventKeyUp, up.Flags);
        }
    }

    [Fact]
    public void A_non_BMP_char_becomes_two_surrogate_units()
    {
        const string clef = "\U0001D11E";
        Assert.Equal(2, clef.Length);

        var keystrokes = UnicodeKeystrokes.Build(clef, pressEnter: false);

        Assert.Equal(4, keystrokes.Count);
        Assert.Equal((ushort)clef[0], keystrokes[0].ScanCode);
        Assert.Equal((ushort)clef[0], keystrokes[1].ScanCode);
        Assert.Equal((ushort)clef[1], keystrokes[2].ScanCode);
        Assert.Equal((ushort)clef[1], keystrokes[3].ScanCode);
    }

    [Fact]
    public void Enter_is_appended_as_VK_RETURN_when_asked()
    {
        var with = UnicodeKeystrokes.Build("x", pressEnter: true);

        Assert.Equal(4, with.Count);
        var down = with[2];
        var up = with[3];
        Assert.Equal(VkReturn, down.VirtualKey);
        Assert.Equal(0u, down.Flags);
        Assert.Equal(VkReturn, up.VirtualKey);
        Assert.Equal(KeyEventKeyUp, up.Flags);

        var without = UnicodeKeystrokes.Build("x", pressEnter: false);
        Assert.DoesNotContain(without, k => k.VirtualKey == VkReturn);
    }
}
