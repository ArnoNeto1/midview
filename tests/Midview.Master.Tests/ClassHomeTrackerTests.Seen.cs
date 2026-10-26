using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassHomeFileMaster's last other home seen (Ruling 23): a tracker's refresh
// that read the Agents before that PC took the room back, and writes after
// this PC kept the newer generation, never takes it back to the older one.
public sealed partial class ClassHomeTrackerTests
{
    [Fact]
    public void TheLastOtherHomeSeen_NeverGoesBackToAnOlderGenerationOfTheSamePc()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"midview-generation-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "class-home.json");
        var tookItBack = new ClassHome(new string('A', 40), ThisPc, "10.0.0.5", ClassFolderRemote.Port, Newer);
        try
        {
            ClassHomeFileMaster.WriteLastOtherHome(tookItBack, path);

            // The same PC (its certificate and name, any case) at an older
            // generation: that generation is kept, the rest is the newest word.
            ClassHomeFileMaster.WriteLastOtherHome(tookItBack with { Name = "prof-1", Address = "10.0.0.6", Generation = Older }, path);
            Assert.Equal(tookItBack with { Name = "prof-1", Address = "10.0.0.6" }, ClassHomeFileMaster.ReadLastOtherHome(path));

            // Newer, it is taken.
            var newer = tookItBack with { Generation = Newer + 1 };
            ClassHomeFileMaster.WriteLastOtherHome(newer, path);
            Assert.Equal(newer, ClassHomeFileMaster.ReadLastOtherHome(path));

            // Another PC (another name under the same certificate, here) replaces it, whatever its generation.
            var other = new ClassHome(new string('A', 40), OtherPc, "10.0.0.7", ClassFolderRemote.Port, Older);
            ClassHomeFileMaster.WriteLastOtherHome(other, path);
            Assert.Equal(other, ClassHomeFileMaster.ReadLastOtherHome(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
