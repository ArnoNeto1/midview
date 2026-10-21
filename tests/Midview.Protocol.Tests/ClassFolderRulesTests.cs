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

    [Fact]
    public void NewAsideName_KeepsTheNameForTheRecycleBin_ButIsNeverListedNorATemp()
    {
        var aside = ClassFolderRules.NewAsideName("trabalho final.docx");

        Assert.EndsWith("-trabalho final.docx", aside);
        Assert.NotEqual(aside, ClassFolderRules.NewAsideName("trabalho final.docx"));
        Assert.False(ClassFolderRules.IsListedFile(aside, 1));
        // Not a temp file: what ends a lesson throws temps away, and this is somebody's work.
        Assert.False(ClassFolderRules.IsTempName(aside));
        Assert.True(aside.Length <= 255);
    }

    [Fact]
    public void FitsTheDepth_CountsTheFileNameAsASegment()
    {
        Assert.True(ClassFolderRules.FitsTheDepth(1));
        Assert.True(ClassFolderRules.FitsTheDepth(FileCollection.MaxDepth));
        Assert.False(ClassFolderRules.FitsTheDepth(FileCollection.MaxDepth + 1));
        // A folder at depth d holds files of d segments, and its subfolders' of d + 1.
        Assert.Equal(ClassFolderRules.FitsTheDepth(FileCollection.MaxDepth), ClassFolderRules.CanDescendFrom(FileCollection.MaxDepth - 1));
    }

    [Theory]
    [InlineData("images.jpg", 2, "", "images (2).jpg")]
    [InlineData("images.jpg", 12, "fotos/", "images (12).jpg")]
    [InlineData("notas", 3, "", "notas (3)")]
    [InlineData("a.b.c", 2, "", "a.b (2).c")]
    [InlineData(".gitignore", 2, "", ".gitignore (2)")]
    [InlineData("relatório final.docx", 2, "", "relatório final (2).docx")]
    public void CopyName_PutsTheCopyNumberBeforeTheExtension(string name, int copy, string prefix, string expected)
    {
        Assert.Equal(expected, ClassFolderRules.CopyName(name, copy, prefix));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(112)]
    [InlineData(116)]
    [InlineData(118)]
    [InlineData(120)]
    public void CopyName_OfALongName_ShortensTheStemSoTheNameIsStillAddressable(int length)
    {
        var name = new string('n', length - 4) + ".jpg";

        foreach (var copy in new[] { 2, 3, 10, 100 })
        {
            var copied = ClassFolderRules.CopyName(name, copy);

            Assert.NotNull(copied);
            Assert.Equal(Math.Min(length + $" ({copy})".Length, FileNames.MaxNameLength), copied!.Length);
            Assert.EndsWith($" ({copy}).jpg", copied);
            Assert.StartsWith("nnnn", copied);
            Assert.True(ClassFolderRules.IsListedFile(copied, 1), copied);
        }
    }

    [Fact]
    public void CopyName_ShortensTheStemForTheWireToo()
    {
        // Four folders of 100 two-byte letters, then a name of 107: 1022 bytes, and 1026 with " (2)".
        var folder = new string('ã', 100) + "/";
        var prefix = string.Concat(Enumerable.Repeat(folder, 4));
        var name = new string('ã', 107) + ".jpg";
        Assert.True(ClassFolderRules.FitsTheWire(prefix + name));

        var copied = ClassFolderRules.CopyName(name, 2, prefix);

        Assert.NotNull(copied);
        Assert.True(ClassFolderRules.FitsTheWire(prefix + copied), "fits the wire");
        Assert.Equal(new string('ã', 106) + " (2).jpg", copied);
    }

    [Fact]
    public void CopyName_NeverCutsASurrogatePairInHalf()
    {
        var name = new string('a', 114) + "\U0001F600" + ".jpg"; // 120 characters, an emoji at the end of the stem

        var copied = ClassFolderRules.CopyName(name, 2);

        Assert.NotNull(copied);
        Assert.True(ClassFolderRules.FitsTheWire(copied!), "no half of a pair is left");
        Assert.Equal(new string('a', 112) + " (2).jpg", copied);
    }

    [Fact]
    public void CopyName_IsNullWhenNoStemCouldMakeItFit()
    {
        var name = "a." + new string('e', 118); // an extension of 119 characters

        Assert.Null(ClassFolderRules.CopyName(name, 2));
    }

    [Theory]
    [InlineData("mapa.png")]
    [InlineData("fotos/aula 1/mapa.png")]
    [InlineData("a/b/c/d/e/f/g/oito.txt")] // 8 segments
    [InlineData("~$pasta/mapa.png")] // a lock file's name is only left out as a file's
    public void WhyNotListed_IsNullForWhatAListingShows(string path)
    {
        Assert.Null(ClassFolderRules.WhyNotListed(path, 10));
    }

    [Theory]
    [InlineData("", 1, "vazio")]
    [InlineData("a/b/c/d/e/f/g/h/nove.txt", 1, "pastas demais")]
    [InlineData("fotos/CON/mapa.png", 1, "nome da pasta")]
    [InlineData("fotos/mapa.png.crdownload", 1, "nome do arquivo")]
    [InlineData("fotos/.midview-x.dat", 1, "nome do arquivo")]
    [InlineData("fotos/~$mapa.docx", 1, "nome do arquivo")]
    [InlineData("mapa.png", FileTransfer.MaxFileSize + 1, "grande demais")]
    public void WhyNotListed_SaysWhyInPortuguese(string path, long size, string expected)
    {
        var reason = ClassFolderRules.WhyNotListed(path, size);

        Assert.NotNull(reason);
        Assert.Contains(expected, reason);
    }

    [Fact]
    public void WhyNotListed_RefusesAPathLongerThanTheWire_AndAnOverlongName()
    {
        var folders = string.Concat(Enumerable.Repeat(new string('ã', 100) + "/", 5)); // 1005 bytes
        Assert.Null(ClassFolderRules.WhyNotListed(folders + "x.txt", 1));

        Assert.Contains("longo demais", ClassFolderRules.WhyNotListed(folders + new string('ã', 12) + ".txt", 1));
        Assert.Contains("nome do arquivo", ClassFolderRules.WhyNotListed(new string('n', FileNames.MaxNameLength + 1) + ".txt", 1));
    }
}
