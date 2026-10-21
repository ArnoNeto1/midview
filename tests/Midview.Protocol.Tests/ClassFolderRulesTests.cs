using Midview.Protocol;
using Xunit;

public class ClassFolderRulesTests
{
    [Theory]
    [InlineData("mapa.png", 10, true)]
    [InlineData("relatório final.docx", 0, true)]
    [InlineData("a~$b.docx", 1, true)] // "~$" only counts at the start
    [InlineData("a.partial", 1, true)] // a suffix is the end of the name, not any part of it
    [InlineData("~$trabalho.docx", 1, false)] // Office's lock file
    [InlineData(".midview-abc.parcial", 1, false)]
    [InlineData(".midview-qualquer.dat", 1, false)] // Midview's own prefix, whatever follows
    [InlineData(".MIDVIEW-abc.dat", 1, false)]
    [InlineData("baixando.crdownload", 1, false)]
    [InlineData("rascunho.TMP", 1, false)]
    [InlineData("meio.part", 1, false)]
    [InlineData("meio.parcial", 1, false)]
    [InlineData("CON", 1, false)] // a name FileNames.Safe would rewrite
    [InlineData("a:b.txt", 1, false)]
    [InlineData("termina em ponto.", 1, false)]
    [InlineData("", 1, false)]
    public void IsListedFile_LeavesOutWhatIsStillBeingWrittenOrCouldNotBeFoundAgain(string name, long size, bool listed)
    {
        Assert.Equal(listed, ClassFolderRules.IsListedFile(name, size));
    }

    [Fact]
    public void IsListedFile_StopsAtTheBiggestFileATransferCarries()
    {
        Assert.True(ClassFolderRules.IsListedFile("video.mp4", FileTransfer.MaxFileSize));
        Assert.False(ClassFolderRules.IsListedFile("video.mp4", FileTransfer.MaxFileSize + 1));
    }

    [Fact]
    public void IsListedFile_RefusesAnOverlongName()
    {
        Assert.True(ClassFolderRules.IsListedFile(new string('n', FileNames.MaxNameLength - 4) + ".txt", 1));
        Assert.False(ClassFolderRules.IsListedFile(new string('n', FileNames.MaxNameLength - 3) + ".txt", 1));
    }

    [Theory]
    [InlineData("relatório.docx", true)]
    [InlineData("3º A", true)]
    [InlineData("Pasta com espaço", true)]
    [InlineData("CON", false)]
    [InlineData("nul.txt", false)]
    [InlineData("x.", false)]
    [InlineData(" x", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("a:b", false)]
    [InlineData("", false)]
    public void IsAddressable_IsANameFileNamesSafeLeavesAlone(string name, bool addressable)
    {
        Assert.Equal(addressable, ClassFolderRules.IsAddressable(name));
    }

    [Fact]
    public void FitsTheWire_IsAtMostMaxNameBytesOfUtf8()
    {
        Assert.True(ClassFolderRules.FitsTheWire("a.txt"));
        Assert.True(ClassFolderRules.FitsTheWire("fotos/mapa.png"));
        Assert.True(ClassFolderRules.FitsTheWire(new string('a', FileTransfer.MaxNameBytes)));
        Assert.False(ClassFolderRules.FitsTheWire(new string('a', FileTransfer.MaxNameBytes + 1)));
        // Bytes, not characters: 'ã' is two.
        Assert.True(ClassFolderRules.FitsTheWire(new string('ã', FileTransfer.MaxNameBytes / 2)));
        Assert.False(ClassFolderRules.FitsTheWire(new string('ã', FileTransfer.MaxNameBytes / 2 + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FitsTheWire_RefusesWhatCouldNotBeSent(string path)
    {
        Assert.False(ClassFolderRules.FitsTheWire(path));
    }

    [Fact]
    public void FitsTheWire_RefusesHalfASurrogatePair_WhichNtfsAllows()
    {
        // Built here: an attribute argument would turn the lone surrogate into U+FFFD.
        Assert.False(ClassFolderRules.FitsTheWire("meio" + '\ud800' + "par.txt"));
        Assert.True(ClassFolderRules.FitsTheWire("meio\U0001F600par.txt")); // a whole pair is fine
    }

    [Fact]
    public void CanDescendFrom_StopsAtMaxDepth_TheFolderItselfBeingDepthOne()
    {
        for (var depth = 1; depth < FileCollection.MaxDepth; depth++)
        {
            Assert.True(ClassFolderRules.CanDescendFrom(depth), $"depth {depth}");
        }
        Assert.False(ClassFolderRules.CanDescendFrom(FileCollection.MaxDepth));
        Assert.False(ClassFolderRules.CanDescendFrom(FileCollection.MaxDepth + 1));
    }

    [Fact]
    public void SkippedSuffixes_AreTheOnesAFileBeingWrittenHas()
    {
        Assert.Equal(new[] { ".crdownload", ".tmp", ".parcial", ".part" }, ClassFolderRules.SkippedSuffixes);
    }

    [Fact]
    public void NewTempName_IsMidviewsOwn_NeverListed_AndNeverTheSameTwice()
    {
        var first = ClassFolderRules.NewTempName();
        var second = ClassFolderRules.NewTempName();

        Assert.NotEqual(first, second);
        Assert.StartsWith(ClassFolderRules.TempPrefix, first);
        Assert.EndsWith(ClassFolderRules.TempSuffix, first);
        Assert.True(ClassFolderRules.IsTempName(first));
        Assert.False(ClassFolderRules.IsListedFile(first, 1));
        Assert.True(ClassFolderRules.IsAddressable(first));
    }

    [Theory]
    [InlineData(".midview-abc.parcial", true)]
    [InlineData(".MIDVIEW-abc.PARCIAL", true)]
    [InlineData(".midview-abc.dat", false)]
    [InlineData("abc.parcial", false)]
    [InlineData("mapa.png", false)]
    public void IsTempName_NeedsBothMarks(string name, bool temp)
    {
        Assert.Equal(temp, ClassFolderRules.IsTempName(name));
    }
}
