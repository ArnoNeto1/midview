using Midview.Protocol;
using Xunit;

public class LastVisitedMruTests
{
    private static readonly byte[] End = { 0xFF, 0xFF, 0xFF, 0xFF };

    private static byte[] Bytes(params byte[] values) => values;

    [Fact]
    public void ParseOrder_ReadsNewestFirst()
    {
        var data = Bytes(2, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF);

        Assert.Equal(new List<int> { 2, 0, 1 }, LastVisitedMru.ParseOrder(data));
    }

    [Fact]
    public void ParseOrder_NoValue_IsEmpty()
    {
        Assert.Empty(LastVisitedMru.ParseOrder(null)!);
    }

    // Each of these makes the list untrustworthy, so the caller leaves the
    // dialog's history alone instead of rewriting it.
    [Theory]
    [InlineData(new byte[] { 1, 0, 0, 0, 0xFF, 0xFF })]                                  // length 6
    [InlineData(new byte[] { 1, 0, 0, 0 })]                                              // no end marker
    [InlineData(new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF })]          // repeated number
    [InlineData(new byte[] { 0x10, 0x27, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF })]                // 10000
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 3, 0, 0, 0 })]                      // data after the end
    [InlineData(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]          // negative as int
    public void ParseOrder_Malformed_IsNull(byte[] data)
    {
        Assert.Null(LastVisitedMru.ParseOrder(data));
    }

    [Fact]
    public void FormatOrder_RoundTrips()
    {
        var data = LastVisitedMru.FormatOrder(new[] { 5, 0 });

        Assert.Equal(new List<int> { 5, 0 }, LastVisitedMru.ParseOrder(data));
        Assert.Equal(End, data[^4..]);
    }

    [Fact]
    public void Entry_ThenProgramOf()
    {
        var idList = Bytes(1, 2, 0, 0);

        var entry = LastVisitedMru.Entry("notepad.exe", idList);

        Assert.Equal("notepad.exe", LastVisitedMru.ProgramOf(entry));
        Assert.Equal(idList, entry[^4..]);
    }

    [Fact]
    public void ProgramOf_NoNull_IsNull()
    {
        var entry = System.Text.Encoding.Unicode.GetBytes(new string('a', 300));

        Assert.Null(LastVisitedMru.ProgramOf(entry));
    }

    [Fact]
    public void Entry_ThenIdListOf()
    {
        var idList = Bytes(1, 2, 3, 0, 0);

        Assert.Equal(idList, LastVisitedMru.IdListOf(LastVisitedMru.Entry("mspaint.exe", idList)));
    }

    [Fact]
    public void IdListOf_NoNull_IsNull()
    {
        var entry = System.Text.Encoding.Unicode.GetBytes(new string('a', 300));

        Assert.Null(LastVisitedMru.IdListOf(entry));
    }
}
