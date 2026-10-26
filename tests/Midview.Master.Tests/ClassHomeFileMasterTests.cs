using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassHomeFileMaster's record of the home this PC took the class folders
// over from while it didn't answer (ReadTakenOverFrom, WriteTakenOverFrom),
// in a temp file: kept beside the file's other records, and anything that
// isn't one reads as none.
public sealed class ClassHomeFileMasterTests : IDisposable
{
    private const long At = 1_790_000_000;

    private static readonly ClassHome OldHome = new(new string('A', 40), "PROF-1", "10.0.0.5", ClassFolderRemote.Port, 1_000);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-class-home-{Guid.NewGuid():N}");

    private string HomePath => Path.Combine(_folder, "class-home.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheHomeTakenOver_IsKept_AndTheOtherRecordsKeepIt()
    {
        var other = new ClassHome(new string('B', 40), "PROF-3", "10.0.0.7", ClassFolderRemote.Port, 2_000);
        Assert.Null(ClassHomeFileMaster.ReadTakenOverFrom(HomePath));

        ClassHomeFileMaster.WriteTakenOverFrom(OldHome, At, HomePath);
        Assert.Equal((OldHome, At), ClassHomeFileMaster.ReadTakenOverFrom(HomePath)!.Value);

        // The file's other writers leave it as it is...
        ClassHomeFileMaster.Write(3_000, HomePath);
        ClassHomeFileMaster.WriteLastOtherHome(other, HomePath);
        ClassHomeFileMaster.WriteHandedOverTo(other, HomePath);
        ClassHomeFileMaster.WriteHandedOverTo(null, HomePath);
        Assert.Equal((OldHome, At), ClassHomeFileMaster.ReadTakenOverFrom(HomePath)!.Value);

        // ...and it leaves theirs.
        ClassHomeFileMaster.WriteTakenOverFrom(null, 0, HomePath);
        Assert.Null(ClassHomeFileMaster.ReadTakenOverFrom(HomePath));
        Assert.Equal(3_000, ClassHomeFileMaster.Read(HomePath));
        Assert.Equal(other, ClassHomeFileMaster.ReadLastOtherHome(HomePath));
    }

    [Theory]
    [InlineData("{\"Generation\":5,\"LastOtherHome\":null}")] // from before it was kept
    [InlineData("{\"Generation\":5,\"LastOtherHome\":null,\"TakenOverFrom\":null}")]
    [InlineData("{\"Generation\":5,\"LastOtherHome\":null,\"TakenOverFrom\":{\"At\":5}}")] // no home
    [InlineData("{\"Generation\":5,\"TakenOverFrom\":{\"Home\":{\"Name\":\"PROF-1\",\"Port\":28903,\"Generation\":1},\"At\":5}}")]
    [InlineData("isto não é json")]
    public void AFileWithNoHomeTakenOver_ReadsAsNone(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(HomePath, text);

        Assert.Null(ClassHomeFileMaster.ReadTakenOverFrom(HomePath));
    }
}
